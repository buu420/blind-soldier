namespace Ff7.Accessibility.Reloaded;

// Character tables are derived from ff7tools/ff7/ff7text.py.
// Copyright (C) Christian Bauer <www.cebix.net>.
// Used under the permission notice reproduced in docs/third-party/ff7tools-notice.md.
internal static class Ff7TextEncoding
{
    public const string Western =
        " !\"#$%&'()*+,-./01234" +
        "56789:;<=>?@ABCDEFGHI" +
        "JKLMNOPQRSTUVWXYZ[\\]^" +
        "_`abcdefghijklmnopqrs" +
        "tuvwxyz{|}~ ÄÅÇÉÑÖÜáà" +
        "âäãåçéèêëíìîïñóòôöõúù" +
        "ûü♥°¢£↔→♪ßα  ´¨≠ÆØ∞±≤" +
        "≥¥µ∂ΣΠπ⌡ªºΩæø¿¡¬√ƒ≈∆«" +
        "»… ÀÃÕŒœ–—“”‘’÷◊ÿŸ⁄ ‹" +
        "›ﬁﬂ■‧‚„‰ÂÊÁËÈÍÎÏÌÓÔ Ò" +
        "ÚÛÙıˆ˜¯˘˙˚¸˝˛ˇ       ";

    public const string Japanese =
        "バばビびブぶベべボぼガがギぎグぐゲげゴごザ" +
        "ざジじズずゼぜゾぞダだヂぢヅづデでドどヴパ" +
        "ぱピぴプぷペぺポぽ0123456789、。" +
        " ハはヒひフふヘへホほカかキきクくケけコこ" +
        "サさシしスすセせソそタたチちツつテてトとウ" +
        "うアあイいエえオおナなニにヌぬネねノのマま" +
        "ミみムむメめモもラらリりルるレれロろヤやユ" +
        "ゆヨよワわンんヲをッっャゃュゅョょァぁィぃ" +
        "ゥぅェぇォぉ!?『』．+ABCDEFGHI" +
        "JKLMNOPQRSTUVWXYZ・*ー〜" +
        "…%/:&【】♥→αβ「」()-=   ⑬";

    private const string KanjiSet1 =
        "必殺技地獄火炎裁雷大怒斬鉄剣槍海衝聖審判転" +
        "生改暗黒釜天崩壊零式自爆使放射臭息死宣告凶" +
        "破晄撃画龍晴点睛超究武神覇癒風邪気封印吹烙" +
        "星守護命鼓動福音掌打水面蹴乱闘合体疾迅明鏡" +
        "止抜山蓋世血祭鎧袖一触者滅森羅万象装備器攻" +
        "魔法召喚獣呼出持相手物確率弱投付与変化片方" +
        "行決定分直前真似覚列後位置防御発回連続敵全" +
        "即効果尾毒消金針乙女興奮剤鎮静能薬英雄榴弾" +
        "右腕砂時計糸戦惑草牙南極冷結晶電鳥角有害質" +
        "爪光月反巨目砲重力球空双野菜実兵単毛茶色髪";

    private const string KanjiSet2 =
        "安香花会員蜂蜜館下着入先不子供屋商品景交換" +
        "階模型部離場所仲間無制限殿様秘氷河図何材料" +
        "雪上進事古代種鍵娘紙町住奥眠楽最初村雨釘陸" +
        "吉揮叢雲軍異常通威父蛇矛青偃刀戟十字裏車円" +
        "輪卍折鶴倶戴螺貝突銀玉正宗具甲烈属性吸収半" +
        "減土高級状態縁闇睡石徐々的指混呪開始歩復盗" +
        "小治理同速遅逃去視複味沈黙還倍数瀕取返人今" +
        "差誰当拡散飛以外暴避振身中旋津波育機械擲炉" +
        "新両本君洞内作警特殊板強穴隊族亡霊鎖足刃頭" +
        "怪奇虫跳侍左首潜長親衛塔宝条像忍謎般見報充" +
        "填完了銃元経験値終獲得名悲蛙操成費背切替割";

    private const string KanjiSet3 =
        "由閉記憶選番街底忘都過艇路運搬船基心港末宿" +
        "西道艦家乗竜巻迷宮絶壁支社久件想秒予多落受" +
        "組余系標起迫日勝形引現解除磁互口廃棄汚染液" +
        "活令副隠主斉登温泉百段熱走急降奪響嵐移危戻" +
        "遠吠軟骨言葉震叫噴舞狩粉失敗眼激盤逆鱗踏喰" +
        "盾叩食凍退木吐線魅押潰曲翼教皇太陽界案挑援" +
        "赤往殴意東北参知聞来仕別集信用思毎悪枯考然" +
        "張好伍早各独配腐話帰永救感故売浮市加流約宇" +
        "礼束母男年待宙立残俺少精士私険関倒休我許郷" +
        "助要問係旧固荒稼良議導夢追説声任柱満未顔旅";

    private const string KanjiSet4 =
        "友伝夜探対調民読占頼若学識業歳争苦織困答準" +
        "恐認客務居他再幸役縮情豊夫近窟責建求迎貸期" +
        "工算湿難保帯届凝笑向可遊襲申次国素題普密望" +
        "官泣創術演輝買途浴老幼利門格原管牧炭彼房驚" +
        "禁注整衆語証深層査渡号科欲店括坑酬緊研権書" +
        "暇兄派造広川賛駅絡在党岸服捜姉敷胸刑谷痛岩" +
        "至勢畑姿統略抹展示修酸製歓接障災室索扉傷録" +
        "優基讐勇司境璧医怖狙協犯資設雇根億脱富躍純" +
        "写病依到練順園総念維検朽圧補公働因朝浪祝恋" +
        "郎勉春功耳恵緑美辺昇悩泊低酒影競二矢瞬希志";

    private const string KanjiSet5 =
        "孫継団給抗違提断島栄油就僕存企比浸非応細承" +
        "編排努締談趣埋営文夏個益損額区寒簡遣例肉博" +
        "幻量昔臓負討悔膨飲妄越憎増枚皆愚療庫涙照冗" +
        "壇坂訳抱薄義騒奴丈捕被概招劣較析繁殖耐論貴" +
        "称千歴史募容噂壱胞鳴表雑職妹氏踊停罪甘健焼" +
        "払侵頃愛便田舎孤晩清際領評課勤謝才偉誤価欠" +
        "寄忙従五送周頑労植施販台度嫌諸習緒誘仮借輩" +
        "席戒弟珍酔試騎霜鉱裕票券専祖惰偶怠罰熟牲燃" +
        "犠快劇拠厄抵適程繰腹橋白処匹杯暑坊週秀看軽" +
        "棊和平王姫庭観航横帳丘亭財律布規謀積刻陥類";

    public static bool TryReadNormal(byte value, bool japanese, out char character)
    {
        var table = japanese ? Japanese : Western;
        if (value >= table.Length)
        {
            character = '\uFFFD';
            return false;
        }

        character = table[value];
        return true;
    }

    public static bool TryReadKanji(byte bank, byte code, out char character)
    {
        var table = bank switch
        {
            0xfa => KanjiSet1,
            0xfb => KanjiSet2,
            0xfc => KanjiSet3,
            0xfd => KanjiSet4,
            0xfe => KanjiSet5,
            _ => string.Empty
        };
        if (code >= table.Length)
        {
            character = '\uFFFD';
            return false;
        }

        character = table[code];
        return true;
    }
}
