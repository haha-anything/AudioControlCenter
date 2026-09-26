using System;
using System.Linq;

namespace BTAudioSwitcher.Models;

/// <summary>
/// 设备品牌识别助手：根据设备名匹配品牌，提供品牌徽章（色块+首字母）与设备类型图标。
/// 识别不到的返回通用图标。
/// </summary>
public static class DeviceBrandHelper
{
    /// <summary>品牌识别结果</summary>
    public readonly record struct BrandInfo(string Key, string DisplayName, string AccentHex, string Icon);

    /// <summary>根据设备名识别品牌（尽量按型号前缀精确匹配）</summary>
    public static BrandInfo Guess(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Generic("📻");

        var n = name.ToLowerInvariant();

        // ---------- 索尼 ----------
        if (n.Contains("srs") || n.Contains("wf-1000") || n.Contains("wf-") || n.Contains("wh-") || n.Contains("wi-")
            || n.Contains("mdr-") || n.Contains("sony") || n.Contains("xb100") || n.Contains("xb20") || n.Contains("xb30"))
            return new BrandInfo("sony", "索尼", "#1E3D8F", n.Contains("srs") || n.Contains("xb") ? "🔊" : "🎧");

        // ---------- 苹果 ----------
        if (n.Contains("airpods") || n.Contains("beats") || n.Contains("find my") || n.Contains("iphone") || n.Contains("ipad"))
            return new BrandInfo("apple", "苹果", "#000000", n.Contains("airpods") ? "🎧" : "🎧");

        // ---------- 三星 ----------
        if (n.Contains("galaxy") || n.Contains("buds2") || n.Contains("buds") || n.Contains("samsung"))
            return new BrandInfo("samsung", "三星", "#1428A0", n.Contains("buds") ? "🎧" : "🔊");

        // ---------- Bose ----------
        if (n.Contains("bose") || n.Contains("qc35") || n.Contains("qc45") || n.Contains("qc ultra") || n.Contains("soundlink") || n.Contains("quietcomfort"))
            return new BrandInfo("bose", "Bose", "#000000", n.Contains("soundlink") || n.Contains("s1") ? "🔊" : "🎧");

        // ---------- JBL ----------
        if (n.Contains("jbl") || n.Contains("charge") || n.Contains("flip") || n.Contains("tune") || n.Contains("live"))
            return new BrandInfo("jbl", "JBL", "#FF6600", n.Contains("charge") || n.Contains("flip") ? "🔊" : "🎧");

        // ---------- 华为 ----------
        if (n.Contains("huawei") || n.Contains("freebuds") || n.Contains("sound x") || n.Contains("soundx"))
            return new BrandInfo("huawei", "华为", "#C7000B", n.Contains("freebuds") ? "🎧" : "🔊");

        // ---------- 小米 ----------
        if (n.Contains("xiaomi") || n.Contains("mi true") || n.Contains("redmi buds") || n.Contains("earbuds"))
            return new BrandInfo("xiaomi", "小米", "#FF6900", "🎧");

        // ---------- 漫步者 ----------
        if (n.Contains("edifier") || n.Contains("漫步者") || n.Contains("w820") || n.Contains("lollipods"))
            return new BrandInfo("edifier", "漫步者", "#003DA5", "🎧");

        // ---------- 罗技 ----------
        if (n.Contains("logitech") || n.Contains("logi"))
            return new BrandInfo("logitech", "罗技", "#00B8FC", n.Contains("g pro") || n.Contains("g733") || n.Contains("g435") ? "🎧" : "🔊");

        // ---------- 雷蛇 ----------
        if (n.Contains("razer") || n.Contains("雷蛇"))
            return new BrandInfo("razer", "雷蛇", "#00FF00", "🎧");

        // ---------- 铁三角 ----------
        if (n.Contains("audio-technica") || n.Contains("铁三角"))
            return new BrandInfo("audiotechnica", "铁三角", "#003DA5", "🎧");

        // ---------- 森海塞尔 ----------
        if (n.Contains("sennheiser") || n.Contains("森海塞尔"))
            return new BrandInfo("sennheiser", "森海塞尔", "#000000", "🎧");

        // ---------- 一加 ----------
        if (n.Contains("oneplus") || n.Contains("一加"))
            return new BrandInfo("oneplus", "一加", "#F50514", "🎧");

        // ---------- OPPO ----------
        if (n.Contains("oppo") || n.Contains("enco"))
            return new BrandInfo("oppo", "OPPO", "#157EFB", "🎧");

        // ---------- 游戏手柄 ----------
        if (n.Contains("dualsense") || n.Contains("dualshock") || n.Contains("xbox") || n.Contains("controller")
            || n.Contains("switch pro") || n.Contains("joy-con"))
            return new BrandInfo("gamepad", "手柄", "#2E7D32", "🎮");

        // ---------- 通用类型 ----------
        if (n.Contains("speaker") || n.Contains("音箱") || n.Contains("soundbar"))
            return Generic("🔊");
        if (n.Contains("headphone") || n.Contains("耳机") || n.Contains("earphone") || n.Contains("headset"))
            return Generic("🎧");
        if (n.Contains("mouse") || n.Contains("鼠标"))
            return Generic("🖱️");
        if (n.Contains("keyboard") || n.Contains("键盘"))
            return Generic("⌨️");
        if (n.Contains("mic") || n.Contains("麦克风"))
            return Generic("🎙️");

        return Generic("📻");
    }

    /// <summary>通用类型图标（无品牌）</summary>
    public static BrandInfo Generic(string icon) => new("generic", "", "#607D8B", icon);

    /// <summary>用户备注品牌时，从已知品牌库取徽章；未知名返回通用</summary>
    public static BrandInfo FromUserBrand(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand)) return Generic("📻");
        var b = Guess(brand); // 按备注名再走一次匹配（用户可能填 Sony/索尼）
        if (b.Key != "generic") return b;
        // 兜底：给个灰色徽章，首字母 = 品牌名首字
        return new BrandInfo("custom", brand, "#607D8B", "📻");
    }

    /// <summary>品牌首字母（徽章上显示，1-2 字符）</summary>
    public static string Initial(string displayName, string key)
    {
        if (!string.IsNullOrEmpty(displayName))
        {
            var s = displayName.Trim();
            return s.Length >= 2 ? s.Substring(0, 1) : s;
        }
        if (!string.IsNullOrEmpty(key)) return key.Substring(0, 1).ToUpperInvariant();
        return "?";
    }
}
