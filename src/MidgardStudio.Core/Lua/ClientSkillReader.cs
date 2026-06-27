namespace MidgardStudio.Core.Lua;

/// <summary>
/// Parses the four client skill files into a <see cref="ClientSkillTables"/>: <c>skillid</c> (the
/// <c>SKID</c> constant→id map), <c>skillinfolist</c> (<c>SKILL_INFO_LIST</c>), <c>skilldescript</c>
/// (<c>SKILL_DESCRIPT</c>) and <c>skilldelaylist</c> (<c>SKILL_DELAY_LIST</c>). All three data tables are
/// keyed by a <c>[SKID.X]</c> expression; skills are aggregated by the constant name (the part after the
/// <c>SKID.</c> prefix). Reuses the shared <see cref="LuaTableParser"/>.
/// </summary>
public static class ClientSkillReader
{
    public static Dictionary<string, int> ReadSkid(string text) => AccessoryTables.ReadConstants(text, "SKID");

    public static ClientSkillTables ReadAll(string skidText, string infoText, string descriptText, string delayText)
    {
        var tables = new ClientSkillTables();
        foreach (var (name, id) in ReadSkid(skidText)) tables.Skid[name] = id;

        ReadInfo(infoText, tables.Skills);
        ReadDescript(descriptText, tables.Skills);
        ReadDelay(delayText, tables.Skills);

        foreach (var (name, skill) in tables.Skills)
        {
            skill.Constant = name;
            skill.Id = tables.Skid.GetValueOrDefault(name);
        }
        return tables;
    }

    public static void ReadInfo(string text, Dictionary<string, ClientSkill> skills)
    {
        var table = new LuaTableParser(text).ParseNamedTable("SKILL_INFO_LIST");
        if (table is null) return;
        foreach (var (key, value) in table.ExprKeys)
        {
            if (value is not LuaTable t) continue;
            var s = GetOrAdd(skills, Strip(key));
            s.HasInfo = true;
            s.Aegis = t.Array.Count > 0 ? t.Array[0] as string ?? string.Empty : string.Empty;
            s.SkillName = t.GetString("SkillName") ?? string.Empty;
            s.MaxLv = t.GetInt("MaxLv");
            s.HasMaxLv = t.NameKeys.ContainsKey("MaxLv");
            s.AttackRange = ReadIntArray(t.GetTable("AttackRange"));
            s.HasAttackRange = t.NameKeys.ContainsKey("AttackRange");
            s.SpAmount = ReadIntArray(t.GetTable("SpAmount"));
            s.HasSpAmount = t.NameKeys.ContainsKey("SpAmount");
            if (t.NameKeys.ContainsKey("bSeperateLv")) s.BSeperateLv = t.GetBool("bSeperateLv");
            s.NeedSkillList = ReadPrereqs(t.GetTable("_NeedSkillList"));
            s.Type = t.GetString("Type");
            s.JobNeedSkillList = ReadJobPrereqs(t.GetTable("NeedSkillList"));
        }
    }

    public static void ReadDescript(string text, Dictionary<string, ClientSkill> skills)
    {
        var table = new LuaTableParser(text).ParseNamedTable("SKILL_DESCRIPT");
        if (table is null) return;
        foreach (var (key, value) in table.ExprKeys)
        {
            if (value is not LuaTable t) continue;
            var s = GetOrAdd(skills, Strip(key));
            s.HasDescript = true;
            s.Description = t.Array.Select(x => x as string ?? x?.ToString() ?? string.Empty).ToList();
        }
    }

    public static void ReadDelay(string text, Dictionary<string, ClientSkill> skills)
    {
        var table = new LuaTableParser(text).ParseNamedTable("SKILL_DELAY_LIST");
        if (table is null) return;
        foreach (var (key, value) in table.ExprKeys)
        {
            if (value is not LuaTable t) continue;
            var s = GetOrAdd(skills, Strip(key));
            s.HasDelay = true;
            s.CastFixedDelay = ReadIntArray(t.GetTable("SkillCastFixedDelay"));
            s.CastStatDelay = ReadIntArray(t.GetTable("SkillCastStatDelay"));
            s.GlobalPostDelay = ReadIntArray(t.GetTable("SkillGlobalPostDelay"));
            s.SinglePostDelay = ReadIntArray(t.GetTable("SkillSinglePostDelay"));
            s.SkillFlag = t.GetTable("SkillFlag")?.Array.Select(x => x as string ?? x?.ToString() ?? string.Empty)
                .Where(x => x.Length > 0).ToList() ?? new List<string>();
        }
    }

    /// <summary>Strips the table prefix from a bracket-expression key: <c>SKID.SM_BASH</c> → <c>SM_BASH</c>.</summary>
    private static string Strip(string exprKey)
    {
        int dot = exprKey.LastIndexOf('.');
        return dot >= 0 ? exprKey[(dot + 1)..] : exprKey;
    }

    private static ClientSkill GetOrAdd(Dictionary<string, ClientSkill> skills, string constant)
    {
        if (!skills.TryGetValue(constant, out var s))
            skills[constant] = s = new ClientSkill { Constant = constant };
        return s;
    }

    private static List<int> ReadIntArray(LuaTable? t)
    {
        var list = new List<int>();
        if (t is null) return list;
        foreach (var x in t.Array)
            if (x is double d) list.Add((int)d);
        return list;
    }

    private static List<SkillPrereq> ReadPrereqs(LuaTable? t)
    {
        var list = new List<SkillPrereq>();
        if (t is null) return list;
        foreach (var item in t.Array)
            if (item is LuaTable pair && pair.Array.Count >= 2 && pair.Array[0] is string skid && pair.Array[1] is double lv)
                list.Add(new SkillPrereq(Strip(skid), (int)lv));
        return list;
    }

    private static List<JobPrereqs> ReadJobPrereqs(LuaTable? t)
    {
        var list = new List<JobPrereqs>();
        if (t is null) return list;
        foreach (var (jobKey, jobVal) in t.ExprKeys)
            if (jobVal is LuaTable jt)
                list.Add(new JobPrereqs(Strip(jobKey), ReadPrereqs(jt)));
        return list;
    }
}
