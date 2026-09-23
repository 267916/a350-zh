// A350 座舱中文 —— 应用器
// 把「值对照.txt」「键对照.txt」套用到 iniBuilds A350 的语言文件上。
// 设计要点：按英文原文匹配，不依赖版本号；iniBuilds 更新后重跑一次即可。
// 编译: csc /target:exe /codepage:65001 /out:A350座舱中文.exe A350CockpitZh.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Text.RegularExpressions;

class A350CockpitZh
{
    const string PkgName = "inibuilds-aircraft-a350";
    const string BakSuffix = ".a350zh.bak";
    const string StateName = "state.txt";

    static string ExeDir { get { return AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'); } }
    static string StatePath { get { return Path.Combine(ExeDir, StateName); } }

    static Dictionary<string, string> LoadTable(string p)
    {
        var h = new Dictionary<string, string>();
        if (!File.Exists(p)) return h;
        foreach (string raw in ReadLinesAuto(p))
        {
            string s = raw.Trim();
            if (s.Length == 0 || s[0] == '#') continue;
            int i = s.IndexOf('=');
            if (i <= 0) continue;
            string k = s.Substring(0, i).Trim();
            string v = s.Substring(i + 1).Trim();
            if (k.Length > 0 && v.Length > 0) h[k] = v;
        }
        return h;
    }

    // 找出 A350 包：先按 UserCfg.opt 里的 InstalledPackagesPath，再退回常见位置
    static string FindPackage()
    {
        var optPaths = new List<string>();
        string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string ad = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        optPaths.Add(Path.Combine(la, @"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\UserCfg.opt"));
        optPaths.Add(Path.Combine(ad, @"Microsoft Flight Simulator 2024\UserCfg.opt"));
        optPaths.Add(Path.Combine(la, @"Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\UserCfg.opt"));
        optPaths.Add(Path.Combine(ad, @"Microsoft Flight Simulator\UserCfg.opt"));

        var roots = new List<string>();
        foreach (string o in optPaths)
        {
            try
            {
                if (!File.Exists(o)) continue;
                foreach (string line in File.ReadAllLines(o))
                {
                    int i = line.IndexOf("InstalledPackagesPath");
                    if (i < 0) continue;
                    int a = line.IndexOf('"', i), b = line.LastIndexOf('"');
                    if (a >= 0 && b > a) roots.Add(line.Substring(a + 1, b - a - 1));
                }
            }
            catch { }
        }

        // 常见默认位置
        roots.Add(Path.Combine(la, @"Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\Packages"));
        roots.Add(Path.Combine(ad, @"Microsoft Flight Simulator 2024\Packages"));

        foreach (string r in roots)
        {
            try
            {
                foreach (string sub in new[] { "Community", "Community2024" })
                {
                    string c = Path.Combine(r, sub, PkgName);
                    if (Directory.Exists(c)) return c;
                }
                string d = Path.Combine(r, PkgName);
                if (Directory.Exists(d)) return d;
            }
            catch { }
        }
        return null;
    }

    static bool HasCjk(string s)
    {
        foreach (char c in s) if (c >= 0x4E00 && c <= 0x9FFF) return true;
        return false;
    }

    static int CountTranslated(string file)
    {
        int n = 0;
        try
        {
            foreach (string l in File.ReadAllLines(file, Encoding.UTF8))
            {
                int c = l.IndexOf(':');
                if (c < 0) continue;
                int q1 = l.IndexOf('"', c), q2 = l.LastIndexOf('"');
                if (q1 < 0 || q2 <= q1) continue;
                if (HasCjk(l.Substring(q1 + 1, q2 - q1 - 1))) n++;
            }
        }
        catch { }
        return n;
    }

    // MSFS 用 layout.json 建立虚拟文件系统。改了语言文件就必须同步这里的大小，
    // 否则模拟器可能忽略改动、甚至拒绝加载包。
    static int FixLayout(string pkg, bool restore, out int inconsistentBefore)
    {
        inconsistentBefore = 0;
        string lj = Path.Combine(pkg, "layout.json");
        if (!File.Exists(lj)) return 0;
        string bak = lj + BakSuffix;

        if (restore)
        {
            if (File.Exists(bak)) { File.Copy(bak, lj, true); return -1; }
            return 0;
        }

        if (!File.Exists(bak)) File.Copy(lj, bak, false);

        string text = File.ReadAllText(lj, Encoding.UTF8);
        int fixedCount = 0;
        int badCount = 0;
        var re = new Regex(@"\{[^{}]*\.(locpak|html)[^{}]*\}", RegexOptions.IgnoreCase);
        text = re.Replace(text, delegate(Match mm)
        {
            string val = mm.Value;
            Match pm = Regex.Match(val, "\"path\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
            if (!pm.Success) return val;
            string full = Path.Combine(pkg, pm.Groups[1].Value.Replace('/', '\\'));
            if (!File.Exists(full)) return val;
            var fi = new FileInfo(full);
            Match sm = Regex.Match(val, "\"size\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
            if (sm.Success && sm.Groups[1].Value != fi.Length.ToString()) badCount++;
            val = Regex.Replace(val, "\"size\"\\s*:\\s*\\d+", "\"size\": " + fi.Length, RegexOptions.IgnoreCase);
            val = Regex.Replace(val, "\"date\"\\s*:\\s*\\d+", "\"date\": " + fi.LastWriteTimeUtc.ToFileTime(), RegexOptions.IgnoreCase);
            fixedCount++;
            return val;
        });
        File.WriteAllText(lj, text, new UTF8Encoding(false));
        inconsistentBefore = badCount;
        return fixedCount;
    }

    // 给 --status 用：数一数 layout.json 里有多少条记录对不上
    static int CountLayoutBad(string pkg)
    {
        try
        {
            string lj = Path.Combine(pkg, "layout.json");
            if (!File.Exists(lj)) return -1;
            string text = File.ReadAllText(lj, Encoding.UTF8);
            int bad = 0;
            foreach (Match mm in new Regex(@"\{[^{}]*\.(locpak|html)[^{}]*\}", RegexOptions.IgnoreCase).Matches(text))
            {
                Match pm = Regex.Match(mm.Value, "\"path\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                Match sm = Regex.Match(mm.Value, "\"size\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase);
                if (!pm.Success || !sm.Success) continue;
                string full = Path.Combine(pkg, pm.Groups[1].Value.Replace('/', '\\'));
                if (!File.Exists(full)) continue;
                if (sm.Groups[1].Value != new FileInfo(full).Length.ToString()) bad++;
            }
            return bad;
        }
        catch { return -1; }
    }
    // ==================== EFB 中文层 ====================
    const string EfbJs = "ini-efb-zh.js";
    const string EfbFont = "A350ZH.ttf";
    const string EfbRel = "html_ui/pages/vcockpit/instruments/ini-efb-a350/" + EfbJs;
    static readonly string[] EfbHtml = { "ini-efb-a350-cpt.html", "ini-efb-a350-fo.html" };
    static readonly string EfbTag =
        "<script type=\"text/javascript\" src=\"/Pages/VCockpit/Instruments/ini-efb-a350/" + EfbJs + "\"></script>";
    // 安装 = 在文件末尾追加这个固定后缀；卸载 = 原样去掉它。字节级可逆，不改动原有内容。
    static readonly string EfbSuffix = "\r\n" + EfbTag + "\r\n";

    static string JsStr(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char ch in s)
        {
            if (ch == '"') sb.Append("\\\"");
            else if (ch == '\\') sb.Append("\\\\");
            else if (ch == '\n') sb.Append("\\n");
            else if (ch == '\r') sb.Append("\\r");
            else if (ch == '\t') sb.Append("\\t");
            else if (ch < 32 || ch > 126) sb.Append("\\u").Append(((int)ch).ToString("x4"));
            else sb.Append(ch);
        }
        sb.Append("\"");
        return sb.ToString();
    }

    // 用 模板 + 词典 合成 ini-efb-zh.js（纯 ASCII，避免编码问题）
    static string BuildEfbJs()
    {
        string tpl = Path.Combine(ExeDir, "efb-zh.template.js");
        string dic = Path.Combine(ExeDir, "efb对照.txt");
        if (!File.Exists(tpl) || !File.Exists(dic)) return null;
        var d = LoadTable(dic);
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var kv in d)
        {
            if (!first) sb.Append(",");
            first = false;
            sb.Append(JsStr(kv.Key)).Append(":").Append(JsStr(kv.Value));
        }
        sb.Append("}");
        string body = ReadTextAuto(tpl);
        // 模板自带的注释里也有中文，一并转义掉，保证输出是纯 ASCII
        var clean = new StringBuilder();
        foreach (char ch in body)
        {
            if (ch > 126) clean.Append("\\u").Append(((int)ch).ToString("x4"));
            else clean.Append(ch);
        }
        return clean.ToString().Replace("/*__DICT__*/{}", sb.ToString());
    }

    static string EfbDir(string pkg)
    {
        return Path.Combine(pkg, @"html_ui\Pages\VCockpit\Instruments\ini-efb-a350");
    }

    // 安装/卸载 EFB 中文层。只动两个 990 字节的 HTML 和新增一个 JS，
    // 完全不碰 ini-efb-a350.js —— 那个文件是 AMDB Bridge 的地盘。
    static int InstallEfb(string pkg, bool restore)
    {
        string dir = EfbDir(pkg);
        if (!Directory.Exists(dir)) return 0;
        int n = 0;
        string target = Path.Combine(dir, EfbJs);

        if (restore)
        {
            if (File.Exists(target)) { File.Delete(target); n++; }
            string fnt = Path.Combine(dir, EfbFont);
            if (File.Exists(fnt)) { File.Delete(fnt); n++; }
            foreach (string h in EfbHtml)
            {
                string p = Path.Combine(dir, h);
                if (!File.Exists(p)) continue;
                string txt = File.ReadAllText(p, Encoding.UTF8);
                if (txt.EndsWith(EfbSuffix))          // 精确去掉追加的后缀
                {
                    File.WriteAllText(p, txt.Substring(0, txt.Length - EfbSuffix.Length), new UTF8Encoding(false));
                    n++;
                }
                else if (txt.IndexOf(EfbJs) >= 0)     // 兜底：后缀被改过时退化为删标签
                {
                    File.WriteAllText(p, txt.Replace(EfbTag, ""), new UTF8Encoding(false));
                    n++;
                }
            }
            return n;
        }

        string js = BuildEfbJs();
        if (js == null) return 0;
        File.WriteAllText(target, js, new UTF8Encoding(false));
        n++;
        string fontSrc = Path.Combine(ExeDir, "fonts", EfbFont);
        if (File.Exists(fontSrc)) { File.Copy(fontSrc, Path.Combine(dir, EfbFont), true); n++; }

        foreach (string h in EfbHtml)
        {
            string p = Path.Combine(dir, h);
            if (!File.Exists(p)) continue;
            string txt = File.ReadAllText(p, Encoding.UTF8);
            if (txt.IndexOf(EfbJs) >= 0) continue;
            File.WriteAllText(p, txt + EfbSuffix, new UTF8Encoding(false));   // 纯追加，不动原有内容
            n++;
        }
        return n;
    }
    // 新增的 JS 必须登记进 layout.json，否则 MSFS 的文件系统里看不到它。
    // 插入时放在数组末尾；移除时连带吃掉前一个逗号，保证 JSON 始终合法。
    static int SyncEfbLayoutEntry(string pkg, bool restore)
    {
        string lj = Path.Combine(pkg, "layout.json");
        if (!File.Exists(lj)) return 0;
        string text = File.ReadAllText(lj, Encoding.UTF8);
        bool present = text.IndexOf(EfbRel, StringComparison.OrdinalIgnoreCase) >= 0;
        string fontRel = "html_ui/pages/vcockpit/instruments/ini-efb-a350/" + EfbFont;
        string fontFull = Path.Combine(pkg, fontRel.Replace('/', '\\'));
        bool fontPresent = text.IndexOf(fontRel, StringComparison.OrdinalIgnoreCase) >= 0;
        if (restore && fontPresent)
        {
            text = Regex.Replace(text, @",\s*\{[^{}]*" + Regex.Escape(EfbFont) + @"[^{}]*\}", "", RegexOptions.IgnoreCase);
            File.WriteAllText(lj, text, new UTF8Encoding(false));
            if (!present) return 1;
            text = File.ReadAllText(lj, Encoding.UTF8);
            fontPresent = false;
        }
        if (!restore && File.Exists(fontFull) && !fontPresent)
        {
            var ffi = new FileInfo(fontFull);
            string fent = "    {\n      \"path\": \"" + fontRel + "\",\n      \"size\": " + ffi.Length +
                          ",\n      \"date\": " + ffi.LastWriteTimeUtc.ToFileTime() + "\n    }";
            int fc = text.LastIndexOf(']');
            int fb = text.LastIndexOf('}', fc);
            if (fb >= 0) { text = text.Insert(fb + 1, ",\n" + fent); File.WriteAllText(lj, text, new UTF8Encoding(false)); }
        }

        if (restore)
        {
            if (!present) return 0;
            text = Regex.Replace(text,
                @",\s*\{[^{}]*" + Regex.Escape(EfbJs) + @"[^{}]*\}",
                "", RegexOptions.IgnoreCase);
            File.WriteAllText(lj, text, new UTF8Encoding(false));
            return 1;
        }

        string full = Path.Combine(pkg, EfbRel.Replace('/', '\\'));
        if (!File.Exists(full) || present) return 0;
        var fi = new FileInfo(full);
        string entry = "    {\n      \"path\": \"" + EfbRel + "\",\n      \"size\": " + fi.Length +
                       ",\n      \"date\": " + fi.LastWriteTimeUtc.ToFileTime() + "\n    }";
        int close = text.LastIndexOf(']');
        int brace = text.LastIndexOf('}', close);
        if (brace < 0) return 0;
        File.WriteAllText(lj, text.Insert(brace + 1, ",\n" + entry), new UTF8Encoding(false));
        return 1;
    }
    // 让 ini-efb-zh.js / A350ZH.ttf 在 layout.json 里的 size/date 跟磁盘保持一致
    static void FixEfbEntrySizes(string pkg)
    {
        try
        {
            string lj = Path.Combine(pkg, "layout.json");
            if (!File.Exists(lj)) return;
            string text = File.ReadAllText(lj, Encoding.UTF8);
            bool changed = false;
            foreach (string nm in new string[] { EfbJs, EfbFont })
            {
                string rel = "html_ui/pages/vcockpit/instruments/ini-efb-a350/" + nm;
                string full = Path.Combine(pkg, rel.Replace('/', '\\'));
                if (!File.Exists(full)) continue;
                Match mm = Regex.Match(text, @"\{[^{}]*" + Regex.Escape(nm) + @"[^{}]*\}", RegexOptions.IgnoreCase);
                if (!mm.Success) continue;
                var fi = new FileInfo(full);
                string upd = Regex.Replace(mm.Value, "\"size\"\\s*:\\s*\\d+", "\"size\": " + fi.Length, RegexOptions.IgnoreCase);
                upd = Regex.Replace(upd, "\"date\"\\s*:\\s*\\d+", "\"date\": " + fi.LastWriteTimeUtc.ToFileTime(), RegexOptions.IgnoreCase);
                if (upd != mm.Value)
                {
                    text = text.Substring(0, mm.Index) + upd + text.Substring(mm.Index + mm.Length);
                    changed = true;
                }
            }
            if (changed) File.WriteAllText(lj, text, new UTF8Encoding(false));
        }
        catch { }
    }
    // ==================== 编码自动识别 ====================
    // 玩家用记事本改词表时可能存成 ANSI/GBK，这里自动识别，避免出现乱码。
    // 顺序：BOM → 严格 UTF-8 → GBK(936) → 系统默认编码
    static string ReadTextAuto(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return new UTF8Encoding(false).GetString(b, 3, b.Length - 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        try
        {
            return new UTF8Encoding(false, true).GetString(b);      // 严格：非法字节直接抛异常
        }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding(936).GetString(b); }   // 退回 GBK
            catch { return Encoding.Default.GetString(b); }
        }
    }

    static string[] ReadLinesAuto(string path)
    {
        return ReadTextAuto(path).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    }
    static void Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }

        bool restore = Array.IndexOf(args, "--restore") >= 0;
        bool status = Array.IndexOf(args, "--status") >= 0;

        Console.WriteLine("A350 座舱中文 —— 应用器");
        Console.WriteLine();

        var val = new Dictionary<string, string>();
        foreach (string tf in Directory.GetFiles(ExeDir, "*对照.txt"))
        {
            if (Path.GetFileName(tf) == "键对照.txt") continue;   // 键级表单独加载
            foreach (var kv in LoadTable(tf)) val[kv.Key] = kv.Value;
        }
        var key = LoadTable(Path.Combine(ExeDir, "键对照.txt"));
        Console.WriteLine("词表：值级 " + val.Count + " 条，键级 " + key.Count + " 条");

        string pkg = FindPackage();
        if (pkg == null)
        {
            Console.WriteLine();
            Console.WriteLine("✗ 找不到 iniBuilds A350。请确认它装在某个 Community 文件夹里。");
            Console.WriteLine("  按任意键退出。");
            try { Console.ReadKey(true); } catch { }
            return;
        }
        Console.WriteLine("A350 位置：" + pkg);

        string ver = "?";
        try
        {
            string mf = Path.Combine(pkg, "manifest.json");
            if (File.Exists(mf))
            {
                string j = File.ReadAllText(mf);
                int i = j.IndexOf("\"package_version\"");
                if (i >= 0)
                {
                    int a = j.IndexOf('"', j.IndexOf(':', i) + 1);
                    int b = j.IndexOf('"', a + 1);
                    if (a >= 0 && b > a) ver = j.Substring(a + 1, b - a - 1);
                }
            }
        }
        catch { }
        Console.WriteLine("A350 版本：" + ver);

        var files = new List<string>(Directory.GetFiles(pkg, "*.locPak"));
        if (files.Count == 0) { Console.WriteLine("✗ 包内没有 .locPak 文件。"); return; }

        string main = Path.Combine(pkg, "en-US.locPak");
        Console.WriteLine();
        Console.WriteLine("当前 en-US.locPak 已翻译：" + CountTranslated(main) + " / 3966 条");
        int lb = CountLayoutBad(pkg);
        Console.WriteLine("layout.json 大小记录：" + (lb == 0 ? "一致" : (lb < 0 ? "无此文件" : lb + " 条不一致，需重新运行本程序")));

        if (status)
        {
            Console.WriteLine();
            bool anyBak = File.Exists(main + BakSuffix);
            Console.WriteLine("备份：" + (anyBak ? "已存在" : "无"));
            if (File.Exists(StatePath)) Console.WriteLine("上次应用：" + File.ReadAllText(StatePath, Encoding.UTF8).Trim());
            Console.WriteLine();
            Console.WriteLine("判断：");
            Console.WriteLine("  已翻译条数 ≈ 2124  → 正常，无需操作");
            Console.WriteLine("  已翻译条数 ≈ 3     → 被官方更新覆盖了，重新运行本程序即可");
            Console.WriteLine();
            try { Console.ReadKey(true); } catch { }
            return;
        }

        int total = 0, nfiles = 0;
        foreach (string f in files)
        {
            string bak = f + BakSuffix;
            if (restore)
            {
                if (File.Exists(bak)) { File.Copy(bak, f, true); nfiles++; }
                continue;
            }
            if (!File.Exists(bak)) File.Copy(f, bak, false);

            string[] lines = File.ReadAllLines(f, Encoding.UTF8);
            var outp = new string[lines.Length];
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                outp[i] = l;
                int c1 = l.IndexOf('"');
                if (c1 < 0) continue;
                int c2 = l.IndexOf('"', c1 + 1);
                if (c2 < 0) continue;
                int colon = l.IndexOf(':', c2);
                if (colon < 0) continue;
                int v1 = l.IndexOf('"', colon);
                if (v1 < 0) continue;
                int v2 = l.LastIndexOf('"');
                if (v2 <= v1) continue;

                string k = l.Substring(c1 + 1, c2 - c1 - 1);
                string v = l.Substring(v1 + 1, v2 - v1 - 1);
                string nw = null;
                if (key.ContainsKey(k)) nw = key[k];
                else if (val.ContainsKey(v)) nw = val[v];
                else if (v.IndexOf("\\/") >= 0 && val.ContainsKey(v.Replace("\\/", "/"))) nw = val[v.Replace("\\/", "/")];
                else if (val.ContainsKey(v.Trim())) nw = val[v.Trim()];
                else if (val.ContainsKey(v.Trim().Replace("\\/", "/"))) nw = val[v.Trim().Replace("\\/", "/")];
                if (nw == null) continue;

                outp[i] = l.Substring(0, v1 + 1) + nw + l.Substring(v2);
                total++;
            }
            File.WriteAllLines(f, outp, new UTF8Encoding(false));
            nfiles++;
        }

        int efbN = InstallEfb(pkg, restore);

        int ljBefore = 0;
        int ljFixed = FixLayout(pkg, restore, out ljBefore);
        SyncEfbLayoutEntry(pkg, restore);
        if (!restore) FixEfbEntrySizes(pkg);

        if (restore)
        {
            Console.WriteLine();
            Console.WriteLine("✓ 已从备份恢复 " + nfiles + " 个语言文件。");
            Console.WriteLine("✓ EFB 中文层已卸载（" + efbN + " 个文件）。");
            Console.WriteLine("✓ layout.json 已还原。");
        }
        else
        {
            try
            {
                File.WriteAllText(StatePath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  版本 " + ver + "  替换 " + (total / Math.Max(files.Count, 1)) + " 条/文件",
                    Encoding.UTF8);
            }
            catch { }
            Console.WriteLine();
            Console.WriteLine("✓ 完成：处理 " + nfiles + " 个语言文件，每个替换 " + (total / Math.Max(files.Count, 1)) + " 条");
            Console.WriteLine("  en-US.locPak 现在已翻译：" + CountTranslated(main) + " / 3966 条");
            Console.WriteLine("  EFB 中文层已安装/更新（" + efbN + " 个文件，含右下角 中/EN 切换按钮）");
            Console.WriteLine("  layout.json 已同步 " + ljFixed + " 条大小记录（修正前不一致 " + ljBefore + " 条）");
            Console.WriteLine();
            Console.WriteLine("  进游戏前请确认 MSFS 语言为「简体中文」，或保持英文均可（两种都已覆盖）。");
        }
        Console.WriteLine();
        Console.WriteLine("按任意键退出。");
        try { Console.ReadKey(true); } catch { }
    }
}
