/* ============================================================
 * A350 EFB 中文层  (ini-efb-zh.js)
 *
 * 原理：只在「渲染层」替换文字节点的内容，不改动 EFB 的任何逻辑代码。
 *   · 词典匹配不上 → 什么都不做（不会出错）
 *   · 全程 try/catch 包裹 → 任何异常都只是静默失效，EFB 照常运行
 *   · 右下角有一个 中/EN 按钮，可随时切换；选择会记住
 *
 * 卸载：删除本文件，并去掉 ini-efb-a350-cpt.html / -fo.html 里那一行 script 即可。
 *
 * 词典由 A350座舱中文.exe 从 efb对照.txt 生成后注入到下方 DICT。
 * ============================================================ */
(function () {
    'use strict';

    var DICT = /*__DICT__*/{};

    // 后缀规则：用少量规则覆盖大量变体（如 GEN 1A FAULT / RMP 2 FAULT ...）
    var RULES = [
        [/ FAULT$/,                                    ' \u6545\u969c'],
        [/ TOGGLED$/,                                  '\uff08\u5df2\u5207\u6362\uff09'],
        [/ SERVICE REQUESTED$/,                        ' \u5df2\u8bf7\u6c42\u52e4\u52a1'],
        [/ SERVICE TOGGLED$/,                          '\u670d\u52a1\uff08\u5df2\u5207\u6362\uff09'],
        [/ IN PROGRESS$/,                              ' \u8fdb\u884c\u4e2d']
    ];

    // EFB 自带字体全是拉丁字形，不换字体中文会显示成方框。
    // A350ZH.ttf 是随本工具附带的 Noto Sans SC 子集（OFL 协议），由工具复制到本目录。
    var FONT_FAMILY = 'A350ZH';
    var FONT_URL = '/Pages/VCockpit/Instruments/ini-efb-a350/A350ZH.ttf';
    var CJK_FONT = '"A350ZH","Microsoft YaHei","\u5fae\u8f6f\u96c5\u9ed1","SimHei",sans-serif';

    function injectFont() {
        try {
            if (document.getElementById('ini-efb-zh-font')) { return; }
            if (!document.head) { return; }
            var st = document.createElement('style');
            st.id = 'ini-efb-zh-font';
            st.type = 'text/css';
            st.appendChild(document.createTextNode(
                '@font-face{font-family:"' + FONT_FAMILY + '";src:url("' + FONT_URL +
                '") format("truetype");font-weight:normal;font-style:normal;}'));
            document.head.appendChild(st);
        } catch (e) { }
    }

    var LS_KEY = 'ini_efb_zh_on';
    var BTN_ID = 'ini-efb-zh-btn';
    var on = true;
    try {
        var saved = window.localStorage ? window.localStorage.getItem(LS_KEY) : null;
        if (saved === '0') { on = false; }
    } catch (e) { }

    function lookup(s) {
        if (!s) { return null; }
        var t = s.replace(/^\s+|\s+$/g, '');
        if (!t) { return null; }
        if (Object.prototype.hasOwnProperty.call(DICT, t)) { return DICT[t]; }
        for (var i = 0; i < RULES.length; i++) {
            if (RULES[i][0].test(t)) { return t.replace(RULES[i][0], RULES[i][1]); }
        }
        return null;
    }

    // 只翻译「整段文字就是词典键」的节点，避免误伤句子里的碎片
    function fixNode(n) {
        try {
            var p = n.parentNode;
            if (!p) { return; }
            var tag = p.nodeName ? p.nodeName.toUpperCase() : '';
            if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'NOSCRIPT' || tag === 'TEXTAREA') { return; }
            if (typeof n.__zhOrig === 'undefined') { n.__zhOrig = n.nodeValue; }
            var orig = n.__zhOrig;
            var want = orig;
            var tr = on ? lookup(orig) : null;
            if (tr) {
                want = orig.replace(orig.replace(/^\s+|\s+$/g, ''), tr);
                // 记住并覆盖字体，否则中文字形会显示成方框
                if (typeof p.__zhFont === "undefined") { p.__zhFont = p.style.fontFamily || ""; }
                try { p.style.setProperty("font-family", CJK_FONT, "important"); } catch (e4) { }
            } else if (typeof p.__zhFont !== "undefined") {
                // 切回英文时还原原本的字体
                try { p.style.removeProperty("font-family"); } catch (e5) { }
                if (p.__zhFont) { p.style.fontFamily = p.__zhFont; }
                p.__zhFont = undefined;
            }
            if (n.nodeValue !== want) { n.nodeValue = want; }
        } catch (e) { }
    }

    function walk(el) {
        if (!el || el.nodeType !== 1) { return; }
        try {
            var nm = el.nodeName ? el.nodeName.toUpperCase() : '';
            if (nm === 'SVG' || nm === 'CANVAS') { return; }
            var cls = el.className;
            if (typeof cls === 'string' && cls.indexOf('leaflet') >= 0) { return; }   // 跳过高频重绘的地图
            var kids = el.childNodes;
            if (!kids) { return; }
            for (var i = 0; i < kids.length; i++) {
                var n = kids[i];
                if (n.nodeType === 3) { fixNode(n); }
                else if (n.nodeType === 1) { walk(n); }
            }
        } catch (e) { }
    }

    function makeButton() {
        try {
            if (document.getElementById(BTN_ID)) { return; }
            var host = document.body || document.documentElement;
            if (!host) { return; }
            var b = document.createElement('div');
            b.id = BTN_ID;
            b.textContent = on ? '\u4e2d' : 'EN';
            b.title = on ? 'Switch EFB to English' : '\u5207\u6362\u4e3a\u4e2d\u6587';
            b.style.cssText =
                'position:fixed;right:6px;bottom:6px;z-index:2147483647;' +
                'width:46px;height:46px;line-height:46px;text-align:center;' +
                'border-radius:23px;background:rgba(0,0,0,0.55);color:#ffffff;' +
                'font-size:22px;font-weight:bold;font-family:sans-serif;' +
                'cursor:pointer;border:1px solid rgba(255,255,255,0.35);' +
                'box-shadow:0 0 8px rgba(0,0,0,0.6);';
            b.onclick = function () {
                on = !on;
                b.textContent = on ? '\u4e2d' : 'EN';
                b.title = on ? 'Switch EFB to English' : '\u5207\u6362\u4e3a\u4e2d\u6587';
                try {
                    if (window.localStorage) { window.localStorage.setItem(LS_KEY, on ? '1' : '0'); }
                } catch (e) { }
                walk(document.body || document.documentElement);
            };
            host.appendChild(b);
        } catch (e) { }
    }

    function tick() {
        try {
            injectFont();
            makeButton();
            walk(document.body || document.documentElement);
        } catch (e) { }
    }

    // 立刻跑一次，之后定时轮询（EFB 是动态渲染的，新页面需要重新翻译）
    try {
        if (document.readyState === 'complete' || document.readyState === 'interactive') {
            window.setTimeout(tick, 300);
        } else {
            document.addEventListener('DOMContentLoaded', function () { window.setTimeout(tick, 300); }, false);
        }
        window.setInterval(tick, 700);
    } catch (e) { }
})();
