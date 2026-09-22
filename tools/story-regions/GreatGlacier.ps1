# The ordinary Great Glacier fields and their reused corridor screens.
# Native LINE geometry, event dispatch and bank1[184] transitions are checked by
# GreatGlacierStoryTests. The route graph selects a decreasing number of crossings
# to a snowfield entrance (world fields61..64), where the existing world Story target
# continues north. This also turns a southbound revisit back toward the story goal.
# Map screen669 and unused685 are not walkable fields. Ice-cave and lake side rooms
# return toward the main route; optional puzzles and treasure are not prerequisites.
# Every LINE keeps native live enable state and must actually be crossed. Ordinary
# corridor self-MAPJUMP loops remain walkable; only the optional ice-floe puzzle
# requires manual puzzle controls between its disconnected islands.
$glacierRoutes = @(
    @{ f=658; name='hyou1'; e=5; entity='line01'; node=-1; line=@(-354,3540,0,-345,3136,0) } # hyou1 -> move_f:0 (14 crossings left)
    @{ f=680; name='hyou10'; e=9; entity='line00d'; node=-1; line=@(-564,1111,257,-484,1111,254) } # hyou10 -> move_d:76 (3 crossings left)
    @{ f=681; name='hyou11'; e=4; entity='line00'; node=-1; line=@(444,2589,286,429,2413,302) } # hyou11 -> move_s:49 (3 crossings left)
    @{ f=682; name='hyou12'; e=1; entity='line00'; node=-1; line=@(623,657,0,623,853,0) } # hyou12 -> world:64 (1 crossings left)
    @{ f=683; name='hyou13_1'; e=6; entity='line00c'; node=-1; line=@(-838,3198,494,-857,3383,525) } # hyou13_1 -> move_r:20 (4 crossings left)
    @{ f=684; name='hyou13_2'; e=3; entity='line00'; node=-1; line=@(-467,853,0,-555,726,0) } # hyou13_2 -> hyou13_1 (5 crossings left)
    @{ f=659; name='hyou2'; e=7; entity='line03'; node=-1; line=@(-994,1536,0,-877,0,0) } # hyou2 -> move_f:3 (12 crossings left)
    @{ f=660; name='hyou3'; e=5; entity='line01'; node=-1; line=@(144,1850,0,128,1536,0) } # hyou3 -> move_r:15 (11 crossings left)
    @{ f=663; name='hyou4'; e=14; entity='line02e'; node=-1; line=@(-1106,1912,338,-936,1989,328) } # hyou4 -> move_s:33 (10 crossings left)
    @{ f=664; name='hyou5_1'; e=6; entity='line00c'; node=-1; line=@(1057,1949,156,1018,1720,165) } # hyou5_1 -> move_f:7 (10 crossings left)
    @{ f=665; name='hyou5_2'; e=11; entity='line00'; node=-1; line=@(-153,-923,31,661,-928,31) } # hyou5_2 -> hyou5_1 (11 crossings left)
    @{ f=666; name='hyou5_3'; e=3; entity='line10'; node=-1; line=@(101,325,0,156,165,0) } # hyou5_3 -> hyou5_4 (13 crossings left)
    @{ f=667; name='hyou5_4'; e=5; entity='line01'; node=-1; line=@(85,257,94,235,257,85) } # hyou5_4 -> hyou5_2 (12 crossings left)
    @{ f=668; name='hyou6'; e=13; entity='line01d'; node=-1; line=@(-1900,1662,165,-1788,1337,165) } # hyou6 -> move_s:47 (5 crossings left)
    @{ f=676; name='hyou7'; e=8; entity='line02'; node=-1; line=@(-745,433,180,-382,-515,180) } # hyou7 -> move_i:23 (8 crossings left)
    @{ f=677; name='hyou8_1'; e=8; entity='line02b'; node=-1; line=@(536,4458,76,724,4443,94) } # hyou8_1 -> move_s:43 (6 crossings left)
    @{ f=678; name='hyou8_2'; e=2; entity='line01'; node=-1; line=@(521,709,0,467,819,0) } # hyou8_2 -> hyou8_1 (7 crossings left)
    @{ f=679; name='hyou9'; e=7; entity='line03'; node=-1; line=@(1475,4298,112,1375,4121,210) } # hyou9 -> move_d:74 (6 crossings left)
    @{ f=661; name='icedun_1'; e=5; entity='line00'; node=-1; line=@(-677,-487,320,-760,-605,320) } # icedun_1 -> hyou3 (12 crossings left)
    @{ f=662; name='icedun_2'; e=2; entity='line00'; node=-1; line=@(-626,-256,911,-626,-112,911) } # icedun_2 -> icedun_1 (13 crossings left)
    @{ f=675; name='move_d'; e=9; entity='line01b'; node=67; line=@(-983,170,855,-991,28,795) } # move_d:67 -> hyou9 (7 crossings left)
    @{ f=675; name='move_d'; e=9; entity='line01b'; node=68; line=@(-983,170,855,-991,28,795) } # move_d:68 -> move_d:67 (8 crossings left)
    @{ f=675; name='move_d'; e=9; entity='line01b'; node=69; line=@(-983,170,855,-991,28,795) } # move_d:69 -> hyou9 (7 crossings left)
    @{ f=675; name='move_d'; e=9; entity='line01b'; node=70; line=@(-983,170,855,-991,28,795) } # move_d:70 -> move_d:69 (8 crossings left)
    @{ f=675; name='move_d'; e=6; entity='line00c'; node=71; line=@(856,-363,-648,794,-478,-671) } # move_d:71 -> hyou7 (9 crossings left)
    @{ f=675; name='move_d'; e=6; entity='line00c'; node=72; line=@(856,-363,-648,794,-478,-671) } # move_d:72 -> move_d:73 (5 crossings left)
    @{ f=675; name='move_d'; e=6; entity='line00c'; node=73; line=@(856,-363,-648,794,-478,-671) } # move_d:73 -> hyou10 (4 crossings left)
    @{ f=675; name='move_d'; e=9; entity='line01b'; node=74; line=@(-983,170,855,-991,28,795) } # move_d:74 -> move_d:75 (5 crossings left)
    @{ f=675; name='move_d'; e=9; entity='line01b'; node=75; line=@(-983,170,855,-991,28,795) } # move_d:75 -> hyou10 (4 crossings left)
    @{ f=675; name='move_d'; e=9; entity='line01b'; node=76; line=@(-983,170,855,-991,28,795) } # move_d:76 -> move_s:54 (2 crossings left)
    @{ f=672; name='move_f'; e=8; entity='line00c'; node=0; line=@(1245,181,131,1235,45,131) } # move_f:0 -> hyou2 (13 crossings left)
    @{ f=672; name='move_f'; e=8; entity='line00c'; node=1; line=@(1245,181,131,1235,45,131) } # move_f:1 -> hyou2 (13 crossings left)
    @{ f=672; name='move_f'; e=13; entity='line01d'; node=3; line=@(-1314,44,136,-1306,-64,134) } # move_f:3 -> hyou4 (11 crossings left)
    @{ f=672; name='move_f'; e=13; entity='line01d'; node=4; line=@(-1314,44,136,-1306,-64,134) } # move_f:4 -> hyou4 (11 crossings left)
    @{ f=672; name='move_f'; e=8; entity='line00c'; node=5; line=@(1245,181,131,1235,45,131) } # move_f:5 -> hyou5_1 (11 crossings left)
    @{ f=672; name='move_f'; e=8; entity='line00c'; node=6; line=@(1245,181,131,1235,45,131) } # move_f:6 -> hyou5_1 (11 crossings left)
    @{ f=672; name='move_f'; e=8; entity='line00c'; node=7; line=@(1245,181,131,1235,45,131) } # move_f:7 -> hyou7 (9 crossings left)
    @{ f=672; name='move_f'; e=8; entity='line00c'; node=8; line=@(1245,181,131,1235,45,131) } # move_f:8 -> hyou7 (9 crossings left)
    @{ f=671; name='move_i'; e=5; entity='line01'; node=21; line=@(-1388,-5099,3164,-1388,-5746,3164) } # move_i:21 -> move_s:55 (2 crossings left)
    @{ f=671; name='move_i'; e=5; entity='line01'; node=22; line=@(-1388,-5099,3164,-1388,-5746,3164) } # move_i:22 -> move_s:56 (2 crossings left)
    @{ f=671; name='move_i'; e=5; entity='line01'; node=23; line=@(-1388,-5099,3164,-1388,-5746,3164) } # move_i:23 -> move_s:37 (7 crossings left)
    @{ f=671; name='move_i'; e=5; entity='line01'; node=24; line=@(-1388,-5099,3164,-1388,-5746,3164) } # move_i:24 -> move_s:38 (7 crossings left)
    @{ f=671; name='move_i'; e=5; entity='line01'; node=25; line=@(-1388,-5099,3164,-1388,-5746,3164) } # move_i:25 -> move_s:35 (8 crossings left)
    @{ f=671; name='move_i'; e=5; entity='line01'; node=26; line=@(-1388,-5099,3164,-1388,-5746,3164) } # move_i:26 -> move_s:36 (8 crossings left)
    @{ f=673; name='move_r'; e=4; entity='line00'; node=10; line=@(1249,-238,54,1259,23,54) } # move_r:10 -> move_r:12 (13 crossings left)
    @{ f=673; name='move_r'; e=4; entity='line00'; node=11; line=@(1249,-238,54,1259,23,54) } # move_r:11 -> hyou3 (12 crossings left)
    @{ f=673; name='move_r'; e=4; entity='line00'; node=12; line=@(1249,-238,54,1259,23,54) } # move_r:12 -> hyou3 (12 crossings left)
    @{ f=673; name='move_r'; e=5; entity='line01'; node=13; line=@(-1332,-44,57,-1304,-474,53) } # move_r:13 -> hyou5_1 (11 crossings left)
    @{ f=673; name='move_r'; e=5; entity='line01'; node=14; line=@(-1332,-44,57,-1304,-474,53) } # move_r:14 -> hyou5_1 (11 crossings left)
    @{ f=673; name='move_r'; e=4; entity='line00'; node=15; line=@(1249,-238,54,1259,23,54) } # move_r:15 -> move_r:17 (10 crossings left)
    @{ f=673; name='move_r'; e=4; entity='line00'; node=16; line=@(1249,-238,54,1259,23,54) } # move_r:16 -> move_r:18 (10 crossings left)
    @{ f=673; name='move_r'; e=4; entity='line00'; node=17; line=@(1249,-238,54,1259,23,54) } # move_r:17 -> move_u:57 (9 crossings left)
    @{ f=673; name='move_r'; e=4; entity='line00'; node=18; line=@(1249,-238,54,1259,23,54) } # move_r:18 -> move_d:68 (9 crossings left)
    @{ f=673; name='move_r'; e=5; entity='line01'; node=19; line=@(-1332,-44,57,-1304,-474,53) } # move_r:19 -> move_i:21 (3 crossings left)
    @{ f=673; name='move_r'; e=5; entity='line01'; node=20; line=@(-1332,-44,57,-1304,-474,53) } # move_r:20 -> move_i:22 (3 crossings left)
    @{ f=673; name='move_r'; e=4; entity='line00'; node=9; line=@(1249,-238,54,1259,23,54) } # move_r:9 -> move_r:11 (13 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=27; line=@(-1333,319,157,-1253,-370,157) } # move_s:27 -> move_s:29 (13 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=28; line=@(-1333,319,157,-1253,-370,157) } # move_s:28 -> move_s:30 (13 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=29; line=@(-1333,319,157,-1253,-370,157) } # move_s:29 -> move_s:31 (12 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=30; line=@(-1333,319,157,-1253,-370,157) } # move_s:30 -> move_s:32 (12 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=31; line=@(-1333,319,157,-1253,-370,157) } # move_s:31 -> hyou4 (11 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=32; line=@(-1333,319,157,-1253,-370,157) } # move_s:32 -> hyou4 (11 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=33; line=@(-1333,319,157,-1253,-370,157) } # move_s:33 -> move_i:25 (9 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=34; line=@(-1333,319,157,-1253,-370,157) } # move_s:34 -> move_i:26 (9 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=35; line=@(-1333,319,157,-1253,-370,157) } # move_s:35 -> hyou8_1 (7 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=36; line=@(-1333,319,157,-1253,-370,157) } # move_s:36 -> hyou8_1 (7 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=37; line=@(-1333,319,157,-1253,-370,157) } # move_s:37 -> hyou6 (6 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=38; line=@(-1333,319,157,-1253,-370,157) } # move_s:38 -> hyou6 (6 crossings left)
    @{ f=670; name='move_s'; e=4; entity='line00'; node=39; line=@(1257,319,157,1195,-370,157) } # move_s:39 -> hyou6 (6 crossings left)
    @{ f=670; name='move_s'; e=4; entity='line00'; node=40; line=@(1257,319,157,1195,-370,157) } # move_s:40 -> hyou6 (6 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=41; line=@(-1333,319,157,-1253,-370,157) } # move_s:41 -> hyou8_1 (7 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=42; line=@(-1333,319,157,-1253,-370,157) } # move_s:42 -> hyou8_1 (7 crossings left)
    @{ f=670; name='move_s'; e=4; entity='line00'; node=43; line=@(1257,319,157,1195,-370,157) } # move_s:43 -> move_s:45 (5 crossings left)
    @{ f=670; name='move_s'; e=4; entity='line00'; node=44; line=@(1257,319,157,1195,-370,157) } # move_s:44 -> move_s:46 (5 crossings left)
    @{ f=670; name='move_s'; e=4; entity='line00'; node=45; line=@(1257,319,157,1195,-370,157) } # move_s:45 -> hyou11 (4 crossings left)
    @{ f=670; name='move_s'; e=4; entity='line00'; node=46; line=@(1257,319,157,1195,-370,157) } # move_s:46 -> hyou11 (4 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=47; line=@(-1333,319,157,-1253,-370,157) } # move_s:47 -> hyou11 (4 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=48; line=@(-1333,319,157,-1253,-370,157) } # move_s:48 -> hyou11 (4 crossings left)
    @{ f=670; name='move_s'; e=4; entity='line00'; node=49; line=@(1257,319,157,1195,-370,157) } # move_s:49 -> move_s:51 (2 crossings left)
    @{ f=670; name='move_s'; e=4; entity='line00'; node=50; line=@(1257,319,157,1195,-370,157) } # move_s:50 -> move_s:52 (2 crossings left)
    @{ f=670; name='move_s'; e=4; entity='line00'; node=51; line=@(1257,319,157,1195,-370,157) } # move_s:51 -> world:61 (1 crossings left)
    @{ f=670; name='move_s'; e=4; entity='line00'; node=52; line=@(1257,319,157,1195,-370,157) } # move_s:52 -> world:61 (1 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=53; line=@(-1333,319,157,-1253,-370,157) } # move_s:53 -> world:62 (1 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=54; line=@(-1333,319,157,-1253,-370,157) } # move_s:54 -> world:62 (1 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=55; line=@(-1333,319,157,-1253,-370,157) } # move_s:55 -> world:63 (1 crossings left)
    @{ f=670; name='move_s'; e=5; entity='line01'; node=56; line=@(-1333,319,157,-1253,-370,157) } # move_s:56 -> world:63 (1 crossings left)
    @{ f=674; name='move_u'; e=4; entity='line00'; node=57; line=@(821,252,1211,903,-212,1103) } # move_u:57 -> move_u:58 (8 crossings left)
    @{ f=674; name='move_u'; e=4; entity='line00'; node=58; line=@(821,252,1211,903,-212,1103) } # move_u:58 -> hyou9 (7 crossings left)
    @{ f=674; name='move_u'; e=5; entity='line01'; node=59; line=@(-1069,-719,-272,-355,-1111,6) } # move_u:59 -> hyou7 (9 crossings left)
    @{ f=674; name='move_u'; e=4; entity='line00'; node=60; line=@(821,252,1211,903,-212,1103) } # move_u:60 -> move_u:61 (8 crossings left)
    @{ f=674; name='move_u'; e=4; entity='line00'; node=61; line=@(821,252,1211,903,-212,1103) } # move_u:61 -> hyou9 (7 crossings left)
    @{ f=674; name='move_u'; e=4; entity='line00'; node=62; line=@(821,252,1211,903,-212,1103) } # move_u:62 -> move_s:53 (2 crossings left)
    @{ f=674; name='move_u'; e=5; entity='line01'; node=63; line=@(-1069,-719,-272,-355,-1111,6) } # move_u:63 -> hyou10 (4 crossings left)
    @{ f=674; name='move_u'; e=5; entity='line01'; node=64; line=@(-1069,-719,-272,-355,-1111,6) } # move_u:64 -> move_u:63 (5 crossings left)
    @{ f=674; name='move_u'; e=4; entity='line00'; node=65; line=@(821,252,1211,903,-212,1103) } # move_u:65 -> hyou13_1 (5 crossings left)
    @{ f=674; name='move_u'; e=4; entity='line00'; node=66; line=@(821,252,1211,903,-212,1103) } # move_u:66 -> move_u:65 (6 crossings left)
)
foreach ($glacierRoute in $glacierRoutes) {
    $glacierLine = $glacierRoute.line
    # hyou5_2 is the optional ice-floe puzzle. Its disconnected islands use the
    # game's directional jump logic, not a continuous walking surface.
    $glacierManual = if ($glacierRoute.f -eq 665) {
        'Use the ice-floe puzzle controls to return to the lake shore. From the shore, use Exits to return to the main glacier route.'
    } else { '' }
    $glacierCondition = $null
    if ($glacierRoute.node -ge 0) {
        $glacierCondition = New-Condition -Bank 1 -Address 184 -Mask 255 -Value $glacierRoute.node
    }
    Add-Definition -FieldId $glacierRoute.f -FieldName $glacierRoute.name -Kind Location `
        -EntityId $glacierRoute.e -EntityName $glacierRoute.entity -ScriptType 'Move/Go' `
        -Label 'Continue through the Great Glacier toward the northern snowfield' `
        -X ([int][Math]::Truncate(($glacierLine[0]+$glacierLine[3])/2)) `
        -Y ([int][Math]::Truncate(($glacierLine[1]+$glacierLine[4])/2)) `
        -Z ([int][Math]::Truncate(($glacierLine[2]+$glacierLine[5])/2)) `
        -MinimumGameMoment 677 -MaximumGameMoment 769 -Priority 0 `
        -ManualNavigationGuidance $glacierManual `
        -RequiredCondition $glacierCondition -RequiredEnabledLineEntityId $glacierRoute.e `
        -UsesPlayerCollisionRadius `
        -TriggerLine ([ordered]@{ startX=$glacierLine[0]; startY=$glacierLine[1]; startZ=$glacierLine[2];
            endX=$glacierLine[3]; endY=$glacierLine[4]; endZ=$glacierLine[5] })
}
Add-CuratedFields 658,659,660,661,662,663,664,665,666,667,668,670,671,672,673,674,675,676,677,678,679,680,681,682,683,684
