using System.Text;
using Utilities.Services;

namespace MidgardStudio.Grf;

/// <summary>
/// Builds in-GRF asset paths. The Korean RO data folders are stored as cp949 bytes; under the 1252
/// display encoding they read back as a specific mojibake string — we reproduce it by encoding the
/// Korean name to cp949 and decoding as 1252, so the path matches the GRF entry exactly.
/// </summary>
public static class GrfAssetPaths
{
    private static string Ansi(string korean) =>
        EncodingService.Ansi.GetString(Encoding.GetEncoding(949).GetBytes(korean));

    private static readonly string Ui = Ansi("유저인터페이스");      // user interface
    private static readonly string ItemFolder = Ansi("아이템");        // item
    private static readonly string AccFolder = Ansi("악세사리");      // accessory
    private static readonly string MonFolder = Ansi("몬스터");        // monster
    private static readonly string Female = Ansi("여");
    private static readonly string Male = Ansi("남");

    public static string ItemIcon(string resourceName) => $@"data\texture\{Ui}\item\{resourceName}.bmp";

    public static string ItemCollection(string resourceName) => $@"data\texture\{Ui}\collection\{resourceName}.bmp";

    public static string DropSprite(string resourceName) => $@"data\sprite\{ItemFolder}\{resourceName}.spr";

    public static string MonsterSprite(string spriteName) => $@"data\sprite\{MonFolder}\{spriteName}.spr";

    public static string HeadgearSpriteFemale(string spriteName) => $@"data\sprite\{AccFolder}\{Female}\{Female}_{spriteName}.spr";

    public static string HeadgearSpriteMale(string spriteName) => $@"data\sprite\{AccFolder}\{Male}\{Male}_{spriteName}.spr";

    /// <summary>In-GRF directory holding the client lua data files.</summary>
    public const string LuaFilesDir = @"data\luafiles514\lua files";
}
