namespace Ff7.Accessibility.Reloaded;

/// <summary>Reviewed native background architecture. Animated actors are described separately.</summary>
public static class RemainingFieldAreaDescriptions
{
    public static IReadOnlyList<FieldCutsceneDescriptionCue> CreateAll() =>
    [
        // 66 fship_1: native base image reviewed; no inferred actor or item state.
        new(66, 0, 0, 10, "An open metal balcony projects from the airship hull. A rounded railing borders the deck between the vessel’s large structural supports.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 67 fship_12: native base image reviewed; no inferred actor or item state.
        new(67, 1, 0, 0, "A metal balcony extends from the curved airship hull. Heavy supports frame the open deck, and a low railing follows its outer edge.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 68 fship_2: native base image reviewed; no inferred actor or item state.
        new(68, -1, -1, -1, "A large curved window overlooks an airship bridge lined with control consoles. A central metal walkway leads between the desks toward the glass.", -1, ObservationOnly: true),
        // 69 fship_22: native base image reviewed; no inferred actor or item state.
        new(69, -1, -1, -1, "Curved control consoles line the airship bridge beneath a vast rounded window. Metal walkways and circular platforms divide the working area.", -1, ObservationOnly: true),
        // 70 fship_23: native base image reviewed; no inferred actor or item state.
        new(70, 0, 0, 5, "The airship bridge opens beneath a huge curved window. Control desks line both sides of a central walkway, with a rounded platform below.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 71 fship_24: native base image reviewed; no inferred actor or item state.
        new(71, 1, 0, 0, "Control consoles flank a metal walkway on the airship bridge. A vast rounded window fills the far wall above the working stations.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 72 fship_25: native base image reviewed; no inferred actor or item state.
        new(72, 0, 0, 3, "A broad metal walkway leads through the airship bridge toward a huge curved window. Control consoles and rounded platforms surround the central aisle.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 73 fship_3: native base image reviewed; no inferred actor or item state.
        new(73, 0, 0, 0, "A long table covered with papers sits on a patterned carpet. Flags, wall panels, and a large screen frame the compact meeting room.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 74 fship_4: native base image reviewed; no inferred actor or item state.
        new(74, 0, 0, 4, "A multi-level industrial room contains metal stairs, railings, and exposed machinery. An overhead sign marks a central doorway, with stacked supplies beside the platforms.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 75 fship_42: native base image reviewed; no inferred actor or item state.
        new(75, 0, 0, 0, "Metal stairs connect narrow platforms in an industrial compartment. Railings border the openings above exposed pipes and machinery, with supplies stacked along the walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 76 fship_5: native base image reviewed; no inferred actor or item state.
        new(76, 0, 0, 0, "A cramped mechanical room holds pipes, gauges, and heavy equipment against rough walls. Bright light shines through a small barred opening.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 77 hill: native base image reviewed; no inferred actor or item state.
        new(77, 0, 0, 0, "A worn rocky hill rises above patches of grass. Low ridges and scattered outcrops break up the uneven landscape.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 86 sea: native base image reviewed; no inferred actor or item state.
        new(86, -1, -1, -1, "A small aircraft floats on a calm body of water. Its broad wings and paired propellers stretch above the reflected surface.", -1, ObservationOnly: true),
        // 87 sky: native base image reviewed; no inferred actor or item state.
        new(87, -1, -1, -1, "The aircraft’s broad wing and paired propellers fill the close view. A rounded cockpit sits beside lettering painted across the wing.", -1, ObservationOnly: true),
        // 116 md1stin: native base image reviewed; no inferred actor or item state.
        new(116, 0, 0, 65, "A narrow railway platform runs beside a train under large overhead pipes. Industrial lights hang above the worn concrete.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 117 md1_1: native base image reviewed; no inferred actor or item state.
        new(117, 0, 0, 11, "Metal walkways, stairs and grated platforms crowd between blocky machinery, tanks and thick overhead pipes.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 118 md1_2: native base image reviewed; no inferred actor or item state.
        new(118, 0, 0, 5, "Grated paths wind between tall tanks and squat machines, with pipes crossing the walls and floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 119 nrthmk: native base image reviewed; no inferred actor or item state.
        new(119, 0, 0, 35, "Narrow metal bridges cross the face of an immense cylindrical tower above a deep shaft, with a glowing opening high in the wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 120 nmkin_1: native base image reviewed; no inferred actor or item state.
        new(120, 0, 0, 91, "Wide metal platforms connect by short stairs inside a chamber lined with pipes. Railings and striped barriers border the machinery.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 121 elevtr1: native base image reviewed; no inferred actor or item state.
        new(121, 0, 0, 36, "Round gauges and small lights cover the back wall of a cramped metal elevator, surrounded by heavy vertical pipes.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 122 nmkin_2: native base image reviewed; no inferred actor or item state.
        new(122, 0, 0, 65, "A huge cylindrical machine rises through several levels of narrow grated walkways. Stairs and ladders connect the platforms above a deep glowing shaft.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 123 nmkin_3: native base image reviewed; no inferred actor or item state.
        new(123, 0, 0, 55, "Narrow stairs and ladders descend through a tall metal shaft, crossing grated platforms under exposed beams and cables.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 124 nmkin_4: native base image reviewed; no inferred actor or item state.
        new(124, 0, 0, 62, "A suspended metal walkway stretches across an open tank, with thick pipes hanging down toward the luminous depths.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 125 nmkin_5: native base image reviewed; no inferred actor or item state.
        new(125, 1, 0, 93, "A large vertical tank with a striped base stands above a grated approach, surrounded by pipes, metal walls and ladders.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 126 southmk1: native base image reviewed; no inferred actor or item state.
        new(126, 0, 0, 4, "Thin bridges cross a deep industrial shaft between tall metal walls and enormous pipes.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 127 southmk2: native base image reviewed; no inferred actor or item state.
        new(127, -1, -1, -1, "A narrow bridge enters a striped doorway in a metal wall, with a second walkway crossing the shaft in front.", -1, ObservationOnly: true),
        // 128 smkin_1: native base image reviewed; no inferred actor or item state.
        new(128, 0, 0, 30, "Raised metal platforms and short stairways form a route through a chamber packed with pipes, railings and striped machinery.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 129 smkin_2: native base image reviewed; no inferred actor or item state.
        new(129, 0, 0, 28, "A huge cylindrical machine rises through several levels of narrow grated walkways. Stairs and ladders connect the platforms above a deep glowing shaft.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 130 smkin_3: native base image reviewed; no inferred actor or item state.
        new(130, 0, 0, 18, "Pipes and girders surround a tall metal shaft. Narrow staircases and grated landings descend between the levels.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 131 smkin_4: native base image reviewed; no inferred actor or item state.
        new(131, 0, 0, 62, "A suspended metal walkway stretches across an open tank, with thick pipes hanging down toward the luminous depths.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 132 smkin_5: native base image reviewed; no inferred actor or item state.
        new(132, 1, 0, 44, "A large vertical tank with a striped base stands above a grated approach, surrounded by pipes, metal walls and ladders.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 133 md8_1: native base image reviewed; no inferred actor or item state.
        new(133, 0, 0, 0, "A narrow street winds between tall buildings, metal stairs, and piled rubble. Overhead beams and a wire fence crowd the dim passage.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 134 md8_2: native base image reviewed; no inferred actor or item state.
        new(134, -1, -1, -1, "A crowded square is lined with tall shopfronts and hanging advertisements. Cobblestones, awnings, and scattered equipment fill the space between the buildings.", -1, ObservationOnly: true),
        // 135 md8_3: native base image reviewed; no inferred actor or item state.
        new(135, -1, -1, -1, "A large circular mechanism fills an industrial shaft. Metal platforms, ladders, and heavy supports surround the machinery on several levels.", -1, ObservationOnly: true),
        // 136 md8_4: native base image reviewed; no inferred actor or item state.
        new(136, -1, -1, -1, "A deep, rough-sided industrial shaft is lined with cables and large pipes. Narrow metal platforms cling to the dark walls.", -1, ObservationOnly: true),
        // 137 md8brdg: native base image reviewed; no inferred actor or item state.
        new(137, 0, 0, 4, "A cobbled bridge crosses between closely packed buildings. Street lamps illuminate the railings and the dark entrances beside the raised walkway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 138 cargoin: native base image reviewed; no inferred actor or item state.
        new(138, 5, 0, 0, "Crates stand along the aisle of a metal cargo compartment. Ribbed walls, ceiling supports, and narrow lights enclose the long interior.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 139 tin_1: native base image reviewed; no inferred actor or item state.
        new(139, 16, 0, 8, "Rows of padded benches face a narrow aisle inside a train carriage. Overhead straps, metal poles, and ceiling lights line the compartment.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 140 tin_2: native base image reviewed; no inferred actor or item state.
        new(140, 9, 0, 0, "A long train carriage contains facing rows of benches beside a central aisle. Curved overhead supports carry hanging straps beneath the ceiling lights.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 141 tin_3: native base image reviewed; no inferred actor or item state.
        new(141, 8, 0, 0, "A train carriage stretches toward a doorway at the far end. Benches line the walls beneath metal poles, hanging straps, and long ceiling lamps.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 142 tin_4: native base image reviewed; no inferred actor or item state.
        new(142, 6, 0, 0, "A narrow train carriage has benches along both walls. A grated strip follows its wooden floor, beneath a row of hanging straps.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 144 mds7st1: native base image reviewed; no inferred actor or item state.
        new(144, 0, 0, 4, "Several parallel railway tracks curve through a crowded industrial yard. Platforms, stairs, pipes, and metal structures fill the spaces between the rails.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 145 mds7st2: native base image reviewed; no inferred actor or item state.
        new(145, 0, 0, 0, "A tangle of railway tracks branches through a busy industrial yard. Metal walkways and scattered machinery stand beside the curving lines.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 146 mds7st3: native base image reviewed; no inferred actor or item state.
        new(146, 1, 0, 0, "A steam locomotive stands beside a broad paved platform. Short stairs descend from its edge, and lamps line the station walkway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 147 mds7st32: native base image reviewed; no inferred actor or item state.
        new(147, -1, -1, -1, "A metal train stands beside a raised stone platform. Tall lamps illuminate its windows, with broad steps leading down from the platform.", -1, ObservationOnly: true),
        // 148 mds7_w1: native base image reviewed; no inferred actor or item state.
        new(148, 1, 0, 0, "A compact industrial room contains a fenced counter, weapon racks, and wall-mounted targets. A narrow staircase rises beside the crowded display area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 149 mds7_w2: native base image reviewed; no inferred actor or item state.
        new(149, 1, 0, 15, "A wire enclosure surrounds stacked wooden crates in a small storage room. Rough metal walls and a bare concrete floor border the cage.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 150 mds7_w3: native base image reviewed; no inferred actor or item state.
        new(150, 0, 0, 0, "Beds and bedding fill a cramped room with corrugated walls. Bare lamps hang above scattered household furnishings and a small patterned rug.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 151 mds7: native base image reviewed; no inferred actor or item state.
        new(151, 7, 0, 0, "Small, improvised buildings crowd around a dirt square. Short stairs, patched metal roofs, illuminated signs, and tall support beams fill the settlement.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 152 mds7_im: native base image reviewed; no inferred actor or item state.
        new(152, 0, 0, 0, "A cramped shop contains a broad counter, tall shelves, and a large cylindrical stove. Containers and stacked supplies cover the walls and floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 153 min71: native base image reviewed; no inferred actor or item state.
        new(153, 3, 0, 0, "A small timber room contains a round table, stools, a bed, and a tall display cabinet. Decorative cloth hangs against one wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 154 mds7pb_1: native base image reviewed; no inferred actor or item state.
        new(154, 6, 0, 14, "A wooden bar counter is lined with round stools. Tables fill the surrounding room beneath shelves, wall decorations, and an illuminated television.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 155 mds7pb_2: native base image reviewed; no inferred actor or item state.
        new(155, 1, 0, 0, "A large table covered with papers fills a compact room. A television shows colored bars, and smaller screens stand against the wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 156 mds7plr1: native base image reviewed; no inferred actor or item state.
        new(156, 1, 0, 0, "A dirt clearing lies beneath an immense metal structure. Scattered debris and low buildings surround the open ground beside a wire fence.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 157 mds7plr2: native base image reviewed; no inferred actor or item state.
        new(157, 12, 0, 0, "An enormous industrial pillar rises from a fenced base. Metal stairs and narrow platforms climb its illuminated cylindrical structure beneath heavy overhead supports.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 158 pillar_1: native base image reviewed; no inferred actor or item state.
        new(158, 0, 0, 21, "A towering metal support is surrounded by stairways and narrow platforms. Rows of small lights extend upward through the vast industrial space.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 159 pillar_2: native base image reviewed; no inferred actor or item state.
        new(159, 0, 0, 12, "A circular grated platform wraps around a massive cylindrical pillar. Railings, lamps, and metal stairways border the machinery above the open shaft.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 160 pillar_3: native base image reviewed; no inferred actor or item state.
        new(160, 0, 0, 26, "A control panel stands on a round grated platform around a huge metal column. A curved railing borders the elevated deck.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 161 tunnel_1: native base image reviewed; no inferred actor or item state.
        new(161, 0, 0, 4, "Railway tracks curve through a dark industrial tunnel. Overhead lamps, thick pipes, and warning lights line the ribbed metal walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 162 tunnel_2: native base image reviewed; no inferred actor or item state.
        new(162, 2, 0, 2, "Rails lead toward an illuminated barrier inside a metal tunnel. Pipes and heavy supports surround the brightly lit opening ahead.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 163 tunnel_3: native base image reviewed; no inferred actor or item state.
        new(163, 0, 0, 4, "A metal doorway stands beside the railway tracks, framed by heavy pipes and warning markings. A danger sign hangs on the adjoining wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 164 sbwy4_1: native base image reviewed; no inferred actor or item state.
        new(164, 1, 0, 0, "A narrow metal passage follows a grated floor between worn industrial walls. Pipes and striped caution markings border the walkway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 165 sbwy4_2: native base image reviewed; no inferred actor or item state.
        new(165, 0, 0, 4, "A long, box-shaped passage runs beneath exposed ceiling pipes. A large distance marking is painted across the wall at the far end.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 166 sbwy4_3: native base image reviewed; no inferred actor or item state.
        new(166, 0, 0, 0, "Metal walkways meet at a bend between ribbed industrial walls. Railings and caution markings border the narrow grated platforms.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 167 sbwy4_4: native base image reviewed; no inferred actor or item state.
        new(167, 4, 0, 0, "A tall steel framework encloses a deep industrial shaft. Ladders, narrow platforms, lamps, and crossing beams rise through the open structure.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 168 sbwy4_5: native base image reviewed; no inferred actor or item state.
        new(168, 1, 0, 0, "A network of grated walkways crosses a dark industrial space. Ladders connect the platforms, and small lights mark their edges.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 169 sbwy4_6: native base image reviewed; no inferred actor or item state.
        new(169, 1, 0, 0, "Narrow grated walkways surround an open rectangular shaft. Ladders climb the metal walls toward bright openings at different heights.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 170 mds5_5: native base image reviewed; no inferred actor or item state.
        new(170, 0, 0, 4, "A broad stretch of cracked bare ground opens before a distant row of improvised buildings. Large structural beams loom above the settlement.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 172 mds5_3: native base image reviewed; no inferred actor or item state.
        new(172, 0, 0, 0, "Small patched huts crowd around a dirt clearing. Corrugated walls, scrap metal, and heavy pipes border the uneven settlement.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 173 mds5_2: native base image reviewed; no inferred actor or item state.
        new(173, 0, 0, 1, "A dirt path winds among small huts built from scrap and sheet metal. Tall pipes and piles of debris crowd the settlement’s edges.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 174 min51_1: native base image reviewed; no inferred actor or item state.
        new(174, 1, 0, 0, "A rough wooden workshop contains tables, crates, machinery, and a large illuminated screen. Heavy braces reinforce the walls around the crowded work area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 175 min51_2: native base image reviewed; no inferred actor or item state.
        new(175, 0, 0, 0, "A narrow room is framed by timber braces and exposed beams. A bed, scattered equipment, and shelves fill the dim interior.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 176 mds5_dk: native base image reviewed; no inferred actor or item state.
        new(176, 1, 0, 0, "A small curved room contains a bed beneath an illuminated monitor. Trophies and containers line shelves along the corrugated walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 177 mds5_1: native base image reviewed; no inferred actor or item state.
        new(177, 4, 0, 0, "Patched houses crowd beneath thick industrial pipes. A large television displaying colored bars hangs above the dirt paths and tangled equipment.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 178 mds5_w: native base image reviewed; no inferred actor or item state.
        new(178, 0, 0, 0, "A cramped workshop is packed with heavy machinery, red pipes, and containers. Small tables and shelves occupy the spaces between the equipment.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 179 mds5_i: native base image reviewed; no inferred actor or item state.
        new(179, 0, 0, 0, "A tiny shop contains a counter, shelves, and stacked boxes. An immense pipe crosses the wooden floor between the furnishings.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 180 mds5_m: native base image reviewed; no inferred actor or item state.
        new(180, 1, 0, 0, "A small wooden room holds a table surrounded by stools. Pipes, containers, wall decorations, and scattered supplies fill the remaining space.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 181 church: native base image reviewed; no inferred actor or item state.
        new(181, 2, 0, 0, "A small steep-roofed church stands among immense industrial pipes and wreckage. A rough dirt path passes its lit windows and narrow entrance.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 182 chrin_1a: native base image reviewed; no inferred actor or item state.
        new(182, 0, 0, 0, "Tall stone columns frame a damaged church interior. A patch of flowers grows through broken floorboards beneath the bright windows.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 183 chrin_1b: native base image reviewed; no inferred actor or item state.
        new(183, 0, 0, 0, "Sunlight enters a ruined church through tall windows. Flowers grow among broken floorboards, with damaged benches and hanging lights around the open nave.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 184 chrin_2: native base image reviewed; no inferred actor or item state.
        new(184, 0, 0, 2, "A narrow staircase climbs beside the church’s stone walls. Heavy roof beams, worn windows, and piles of debris surround the upper passage.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 185 chrin_3a: native base image reviewed; no inferred actor or item state.
        new(185, 0, 0, 4, "Heavy timber beams cross high above the church floor. Tall windows and the flower patch below are visible through gaps in the structure.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 186 chrin_3b: native base image reviewed; no inferred actor or item state.
        new(186, 0, 0, 4, "Broken roof beams cross beneath a large hole in the church roof. Bright daylight pours through the jagged opening above the nave.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 187 eals_1: native base image reviewed; no inferred actor or item state.
        new(187, 0, 0, 0, "Dense flower beds cover terraced ground beside a house. Short wooden fences and walkways border the gardens beneath a tall waterfall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 188 ealin_1: native base image reviewed; no inferred actor or item state.
        new(188, 0, 0, 46, "A round dining table stands in a cozy room filled with flowers. Wooden furnishings, stairs, and a patterned rug surround the central seating area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 189 ealin_12: native base image reviewed; no inferred actor or item state.
        new(189, -1, -1, -1, "A round table and chairs sit beside a broad wooden staircase. Flowers decorate the shelves and cabinets along the room’s walls.", -1, ObservationOnly: true),
        // 190 ealin_2: native base image reviewed; no inferred actor or item state.
        new(190, 0, 0, 15, "A compact upstairs room contains a bed, shelves, and flower-filled containers. A wooden landing borders the stairwell beside the bedroom.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 191 mds6_1: native base image reviewed; no inferred actor or item state.
        new(191, 0, 0, 4, "Construction equipment stands amid broken roads and exposed dirt. Heavy concrete slabs, pipes, and a high wall surround the uneven work site.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 192 mds6_2: native base image reviewed; no inferred actor or item state.
        new(192, 0, 0, 18, "Scattered playground structures stand amid piles of debris and broken machinery. A rough dirt path passes through the crowded, enclosed space.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 193 mds6_22: native base image reviewed; no inferred actor or item state.
        new(193, 0, 0, 31, "Playground structures and discarded equipment crowd a rough dirt passage. Piles of scrap and damaged supports rise along both sides.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 194 mds6_3: native base image reviewed; no inferred actor or item state.
        new(194, 0, 0, 2, "A broad dirt clearing is hemmed in by scrap-metal huts and tall pipes. Jagged roofs and exposed beams crowd its edges.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 195 mrkt2: native base image reviewed; no inferred actor or item state.
        new(195, 0, 0, 2, "A long, crowded market street winds between illuminated shops. Signs, lamps, patched roofs, and narrow alleys fill the tightly packed district.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 196 mkt_w: native base image reviewed; no inferred actor or item state.
        new(196, 0, 0, 2, "A dim shop contains display counters, shelves, and scattered equipment. Brightly lit cabinets stand along the walls beside the narrow floor space.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 197 mkt_mens: native base image reviewed; no inferred actor or item state.
        new(197, 0, 0, 2, "A boxing ring occupies one side of a wooden gym. Training equipment, benches, and a low table fill the surrounding room.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 198 mkt_ia: native base image reviewed; no inferred actor or item state.
        new(198, 0, 0, 0, "A tall, rounded shop is lined with containers on curved shelves. Lamps and a bright display illuminate the crowded ground floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 199 mktinn: native base image reviewed; no inferred actor or item state.
        new(199, 0, 0, 0, "Several beds occupy adjoining rooms around a narrow inn passage. Wooden floors, small tables, lamps, and storage cabinets fill the compact interior.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 200 mkt_m: native base image reviewed; no inferred actor or item state.
        new(200, 0, 0, 0, "A long shop counter stands beneath rows of hanging tags. Shelves, decorative furnishings, and small containers crowd the dim room.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 201 mkt_s1: native base image reviewed; no inferred actor or item state.
        new(201, 0, 0, 2, "A small shop contains display shelves and a wooden counter beside a patterned rug. Clothing and other merchandise fill the walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 202 mkt_s2: native base image reviewed; no inferred actor or item state.
        new(202, 0, 0, 2, "A long dining counter is lined with round stools. Cooking equipment, containers, and shelves fill the narrow workspace behind it.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 203 mkt_s3: native base image reviewed; no inferred actor or item state.
        new(203, 0, 0, 2, "A compact shop contains stacked containers, shelves, and a long display counter. Worn floor tiles fill the open space between the merchandise.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 204 mktpb: native base image reviewed; no inferred actor or item state.
        new(204, 0, 0, 2, "Small dining tables and stools fill a dim restaurant. Decorative furnishings surround the seating area beneath hanging lights and a wall-mounted screen.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 205 mrkt1: native base image reviewed; no inferred actor or item state.
        new(205, 0, 0, 2, "A narrow market street winds between crowded shopfronts and illuminated signs. Patched roofs and small entrances line the uneven passage.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 206 colne_1: native base image reviewed; no inferred actor or item state.
        new(206, 5, 0, 0, "A crowded courtyard leads toward a large raised entrance. Neon signs, lanterns, awnings, and ornamental fixtures fill the surrounding walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 207 colne_2: native base image reviewed; no inferred actor or item state.
        new(207, 0, 0, 17, "Twin stairways rise through an ornate hall lined with pillars and hanging banners. A large emblem decorates the circular floor beneath the upper landing.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 208 colne_3: native base image reviewed; no inferred actor or item state.
        new(208, 4, 0, 0, "Low tables and cushions fill an ornate room. Cabinets, framed decorations, and household furnishings crowd the patterned floor beneath the dim walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 209 colne_4: native base image reviewed; no inferred actor or item state.
        new(209, 5, 0, 0, "A long, narrow staircase descends into a small stone room. Pipes, wall fixtures, and a low central platform fill the space below.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 210 colne_5: native base image reviewed; no inferred actor or item state.
        new(210, 0, 0, 0, "An ornate wooden room contains a patterned central carpet, stools, and a raised desk. Decorative statues and wall hangings frame the furnishings.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 211 colne_6: native base image reviewed; no inferred actor or item state.
        new(211, 4, 0, 0, "A large bed fills an extravagantly decorated bedroom. Tall lanterns, mirrors, and colorful ornamental fixtures surround the raised sleeping area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 212 colne_b1: native base image reviewed; no inferred actor or item state.
        new(212, 0, 0, 4, "A narrow service passage bends around a large square structure. Pipes and worn metal walls border the shallow channel beside the walkway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 213 colne_b3: native base image reviewed; no inferred actor or item state.
        new(213, 0, 0, 34, "A narrow industrial passage runs beside exposed pipes and metal walls. Railings and grated sections divide the cramped walkway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 214 mrkt3: native base image reviewed; no inferred actor or item state.
        new(214, 2, 0, 0, "An elaborately decorated building entrance is surrounded by illuminated signs, lanterns, and ornamental fixtures. A short path leads between its glowing pillars.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 216 onna_2: native base image reviewed; no inferred actor or item state.
        new(216, 1, 0, 0, "A round table and chairs stand in a richly decorated room. Three mirrors line a dressing counter, with a barred doorway beside them.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 219 onna_5: native base image reviewed; no inferred actor or item state.
        new(219, 1, 0, 0, "A circular bed stands beside a small tiled bath. Draped curtains, mirrors, and an ornate doorway decorate the surrounding room.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 220 onna_52: native base image reviewed; no inferred actor or item state.
        new(220, 2, 0, 0, "A round bed occupies a brightly decorated room beside a tiled bath. Curved wall patterns, mirrors, and an ornate doorway frame the furnishings.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 222 mrkt4: native base image reviewed; no inferred actor or item state.
        new(222, 0, 0, 0, "A tall, cracked wall is covered with overlapping graffiti. The close view shows rough concrete and painted lettering across its worn surface.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 223 wcrimb_1: native base image reviewed; no inferred actor or item state.
        new(223, 0, 0, 0, "Immense pipes rise through a dark industrial shaft. Narrow platforms and metal fixtures cling to the crowded structure at different heights.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 224 wcrimb_2: native base image reviewed; no inferred actor or item state.
        new(224, 0, 0, 0, "Large pipes and heavy supports extend through a deep, dark industrial space. Small lights illuminate the narrow structures between them.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 225 md0: native base image reviewed; no inferred actor or item state.
        new(225, 0, 0, 0, "A massive cylindrical tower rises above thick pipes and stacked industrial machinery. The close upward view emphasizes its illuminated bands and tall central structure.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 226 roadend: native base image reviewed; no inferred actor or item state.
        new(226, 0, 0, 4, "An elevated road ends above a distant landscape under an open sky. Railings, lamps, and exposed supports border the narrow roadway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 227 sinbil_1: native base image reviewed; no inferred actor or item state.
        new(227, 2, 0, 10, "Broad steps lead toward the curved glass facade of a large office tower. Illuminated windows rise above its emblem and paved entrance plaza.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 228 sinbil_2: native base image reviewed; no inferred actor or item state.
        new(228, 0, 0, 2, "A narrow metal passage runs beside an immense exposed pipe. Caution markings line the floor beneath small wall lights and heavy supports.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 229 blinst_1: native base image reviewed; no inferred actor or item state.
        new(229, 1, 0, 0, "A tall enclosed stairwell zigzags upward across many identical landings. Metal doors and wall panels repeat beside the sharply angled flights.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 231 blinst_3: native base image reviewed; no inferred actor or item state.
        new(231, 1, 0, 0, "A narrow stairwell contains several sharply angled flights connecting stacked landings. Worn metal walls and doors enclose the repeated levels.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 232 blinele: native base image reviewed; no inferred actor or item state.
        new(232, 10, 0, 27, "A square elevator lobby contains grated floor panels and a broad doorway. Red signs and a wall-mounted screen hang above the metal fixtures.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 234 blin1: native base image reviewed; no inferred actor or item state.
        new(234, 8, 0, 5, "Curved stairways flank a central reception desk in a spacious office lobby. Illuminated displays and polished floors surround the open entrance area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 235 blin2: native base image reviewed; no inferred actor or item state.
        new(235, 8, 0, 0, "A broad upper landing wraps around an open lobby. Twin stairways, wall displays, and cylindrical fixtures border the polished floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 236 blin2_i: native base image reviewed; no inferred actor or item state.
        new(236, 5, 0, 0, "A circular display stands beside rows of tall cylindrical exhibits. Illuminated cabinets and a long counter line the room’s walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 237 blin3_1: native base image reviewed; no inferred actor or item state.
        new(237, 7, 0, 0, "A broad counter encloses part of an upper office lobby. Columns, illuminated fixtures, and large screens frame the polished floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 238 blin59: native base image reviewed; no inferred actor or item state.
        new(238, 8, 0, 5, "A square elevator opening sits in the center of a broad landing. Curved walls, doors, and small illuminated panels surround the tiled floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 239 blin60_1: native base image reviewed; no inferred actor or item state.
        new(239, 6, 0, 0, "Broad corridors connect compact rooms around two central stairways. Rows of small planters and illuminated panels line the office floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 240 blin60_2: native base image reviewed; no inferred actor or item state.
        new(240, 4, 0, 0, "A bank of elevator doors stands beside a thick metal support. Small illuminated indicators hang above the entrances along a narrow carpeted strip.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 241 blin61: native base image reviewed; no inferred actor or item state.
        new(241, 7, 0, 0, "A large tree rises in the middle of a circular office lounge. Curved benches and small tables surround it beneath rows of tall windows.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 242 blin62_1: native base image reviewed; no inferred actor or item state.
        new(242, 2, 0, 2, "Glass-walled rooms of bookshelves occupy the center of an office floor. Narrow passages connect them to surrounding rooms, elevators, and stairs.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 243 blin62_2: native base image reviewed; no inferred actor or item state.
        new(243, 4, 0, 2, "Rows of bookcases stand inside a bright glass-walled room. A narrow central aisle divides the shelves beneath evenly spaced ceiling lights.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 244 blin62_3: native base image reviewed; no inferred actor or item state.
        new(244, -1, -1, -1, "Bookshelves line both sides of a narrow central aisle inside a glass-walled room. Bright ceiling lights illuminate the rows and patterned floor.", -1, ObservationOnly: true),
        // 245 blin63_1: native base image reviewed; no inferred actor or item state.
        new(245, 6, 0, 0, "Small rooms and storage compartments line an office floor. Narrow passages pass crates, doors, and wall-mounted controls around the central enclosed spaces.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 246 blin63_t: native base image reviewed; no inferred actor or item state.
        new(246, 5, 0, 0, "A narrow metal walkway bends around enclosed rooms beneath the ceiling. The elevated passage overlooks the office floor through gaps in its structure.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 247 blin64: native base image reviewed; no inferred actor or item state.
        new(247, 10, 0, 0, "An office floor contains a locker room, shelves, and a bright exercise area. Several compact rooms connect through narrow passages and elevator landings.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 248 blin65_1: native base image reviewed; no inferred actor or item state.
        new(248, 2, 0, 0, "A large circular chamber fills the center of an office floor. Smaller rooms and corridors surround its deep, illuminated display space.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 249 blin65_2: native base image reviewed; no inferred actor or item state.
        new(249, 3, 0, 2, "A detailed circular structure occupies a deep round display chamber. Tall segmented walls and small illuminated fixtures surround the central installation.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 250 blin66_1: native base image reviewed; no inferred actor or item state.
        new(250, 0, 0, 44, "A long conference table fills the center of an office floor. Smaller rooms, corridors, and elevator doors surround the brightly lit meeting space.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 251 blin66_2: native base image reviewed; no inferred actor or item state.
        new(251, 0, 0, 32, "A long conference table stands beneath high wall lights. Rows of chairs face a raised central display along the table’s length.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 252 blin66_3: native base image reviewed; no inferred actor or item state.
        new(252, 0, 0, 9, "A narrow tiled restroom contains a toilet beneath a bright overhead light. A slim partition and pale walls enclose the small space.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 253 blin66_4: native base image reviewed; no inferred actor or item state.
        new(253, 0, 0, 22, "A small shaft-like compartment ends at a closed metal door. Light falls through a grated ceiling onto the confined floor below.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 254 blin66_5: native base image reviewed; no inferred actor or item state.
        new(254, 0, 0, 4, "A heavy metal grille frames a view into a brightly lit room below. The slats cross above a glowing central display.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 255 blin66_6: native base image reviewed; no inferred actor or item state.
        new(255, 2, 0, 0, "A long conference table fills a brightly lit meeting room. Chairs line both sides, with a raised display running along the center.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 256 blin67_1: native base image reviewed; no inferred actor or item state.
        new(256, 0, 0, 28, "A laboratory floor is divided into rooms containing machinery, equipment, and storage boxes. Narrow metal passages connect the work areas and small compartments.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 257 blin671b: native base image reviewed; no inferred actor or item state.
        new(257, 0, 0, 31, "A crowded laboratory floor contains heavy machinery, stacked equipment, and small enclosed rooms. Narrow passages connect the separated work areas.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 258 blin67_2: native base image reviewed; no inferred actor or item state.
        new(258, 0, 0, 167, "Several compact cell-like rooms line a narrow upper passage. Heavy doors, small indicators, and overhead lights frame the enclosed spaces.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 259 blin67_3: native base image reviewed; no inferred actor or item state.
        new(259, 0, 0, 4, "Wooden crates crowd a laboratory walkway beside large cylindrical tanks. Metal platforms, control panels, and thick machinery fill the surrounding space.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 260 blin673b: native base image reviewed; no inferred actor or item state.
        new(260, 0, 0, 7, "Crates line a laboratory walkway beside cylindrical tanks and heavy machinery. A dark broken opening tears through a large structure against the far wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 262 blin68_1: native base image reviewed; no inferred actor or item state.
        new(262, 0, 0, 66, "A large cylindrical apparatus stands at the center of a broad laboratory. Metal walkways, consoles, and curved equipment surround its circular platform.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 263 blin68_2: native base image reviewed; no inferred actor or item state.
        new(263, 0, 0, 23, "A tall glass cylinder stands on a circular platform surrounded by caution markings. Railings, pipes, and control panels frame the nearby walkway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 264 blin69_1: native base image reviewed; no inferred actor or item state.
        new(264, 0, 0, 163, "Curved passages connect a broad office floor with small rooms and elevator doors. Seating areas and low partitions border the central spaces.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 266 blin70_1: native base image reviewed; no inferred actor or item state.
        new(266, 0, 0, 66, "A spacious upper floor contains tall columns and a large curved desk. Polished floors, low seating, and broad windows surround the open central area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 267 blin70_2: native base image reviewed; no inferred actor or item state.
        new(267, 0, 0, 18, "A broad curved desk stands beneath a luminous canopy supported by tall columns. Large windows surround the polished office floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 268 blin70_3: native base image reviewed; no inferred actor or item state.
        new(268, 0, 0, 35, "A narrow outdoor ledge follows the curved wall of a tall building. Low barriers and metal railings border the elevated platform.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 269 blin70_4: native base image reviewed; no inferred actor or item state.
        new(269, 0, 0, 4, "Tall columns frame a dark office with broad windows. A chair with a high back and a curved desk stand against the expansive glass wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 270 niv_w: native base image reviewed; no inferred actor or item state.
        new(270, 0, 0, 0, "A small wooden shop is packed with barrels, shelves, containers, and scattered equipment. A crowded counter divides the dim interior.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 271 nvmin1_1: native base image reviewed; no inferred actor or item state.
        new(271, 0, 0, 0, "A cozy wooden room contains a low table, patterned rugs, and a large stove. Shelves and household furnishings line the walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 272 nvmin1_2: native base image reviewed; no inferred actor or item state.
        new(272, -1, -1, -1, "Two simple chairs stand near a dark fireplace in a small room. Exposed pipes, a rug, and containers frame the cramped interior.", -1, ObservationOnly: true),
        // 273 nivinn_1: native base image reviewed; no inferred actor or item state.
        new(273, 0, 0, 0, "A wooden inn contains a broad entrance room and a smaller back room. A curved counter, stairs, and tables fill the connected interior.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 275 nivinn_3: native base image reviewed; no inferred actor or item state.
        new(275, -1, -1, -1, "Beds line a broad wooden room beneath illuminated windows. Patterned rugs and a round table divide the open sleeping area.", -1, ObservationOnly: true),
        // 276 niv_cl: native base image reviewed; no inferred actor or item state.
        new(276, 0, 0, 0, "A wooden home connects a stone-floored kitchen with smaller living spaces. A central staircase rises beside tables, cabinets, and household furnishings.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 277 trackin: native base image reviewed; no inferred actor or item state.
        new(277, -1, -1, -1, "Wooden crates and long containers line a cramped covered compartment. Curving pipes frame a pair of doors at the far end.", -1, ObservationOnly: true),
        // 278 trackin2: native base image reviewed; no inferred actor or item state.
        new(278, -1, -1, -1, "A narrow covered compartment contains crates and long storage containers. Metal pipes curve overhead toward double doors at the far wall.", -1, ObservationOnly: true),
        // 279 nivgate: native base image reviewed; no inferred actor or item state.
        new(279, -1, -1, -1, "A dirt road passes timber houses, barrels, and short white fences. Tall street lamps line the passage between the closely spaced buildings.", -1, ObservationOnly: true),
        // 280 nivgate2: native base image reviewed; no inferred actor or item state.
        new(280, -1, -1, -1, "A narrow street passes timber houses and low fences. Lamps, barrels, and closely spaced roofs frame the uneven road.", -1, ObservationOnly: true),
        // 281 nivgate3: native base image reviewed; no inferred actor or item state.
        new(281, -1, -1, -1, "Timber houses crowd beside a narrow road bordered by low fences. Street lamps and barrels stand between the closely spaced buildings.", -1, ObservationOnly: true),
        // 282 nivl: native base image reviewed; no inferred actor or item state.
        new(282, 0, 0, 0, "Timber houses surround a broad town square with a central raised structure. Pipes and tall cylindrical fixtures stand among the clustered roofs.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 283 nivl_2: native base image reviewed; no inferred actor or item state.
        new(283, -1, -1, -1, "A broad square opens between timber houses and closely packed roofs. Cylindrical fixtures and thick pipes rise beside a central raised structure.", -1, ObservationOnly: true),
        // 285 nivl_4: native base image reviewed; no inferred actor or item state.
        new(285, -1, -1, -1, "Timber houses and exposed pipes surround an open town square. A raised central structure stands between the clustered roofs and narrow streets.", -1, ObservationOnly: true),
        // 288 niv_ti3: native base image reviewed; no inferred actor or item state.
        new(288, 0, 0, 3, "Sunlight falls across wooden floorboards beneath a broad window. Curtains, a flower vase, and a small wall lamp frame the bright interior.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 290 nivl_b1: native base image reviewed; no inferred actor or item state.
        new(290, 0, 0, 5, "Flames illuminate a town square surrounded by timber houses. Burning roofs and scattered debris fill the space around the central raised structure.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 291 nivl_b12: native base image reviewed; no inferred actor or item state.
        new(291, 0, 0, 3, "Fire lights the closely packed timber buildings around a town square. Burning roofs, debris, and exposed pipes surround the central open ground.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 292 nivl_b2: native base image reviewed; no inferred actor or item state.
        new(292, 0, 0, 3, "Flames light a narrow street between timber houses. Broken beams, debris, and cylindrical fixtures crowd the road beside the burning buildings.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 293 nivl_b22: native base image reviewed; no inferred actor or item state.
        new(293, 0, 0, 3, "A narrow town street is illuminated by flames around timber houses. Broken supports and scattered debris lie between the burning structures.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 294 nivl_e1: native base image reviewed; no inferred actor or item state.
        new(294, -1, -1, -1, "A bright star-filled sky stretches above the silhouettes of rooftops and dark hills. Small lights glow among the buildings below.", -1, ObservationOnly: true),
        // 295 nivl_e2: native base image reviewed; no inferred actor or item state.
        new(295, -1, -1, -1, "Stars fill the night sky above rooftops and dark hills. A central raised structure and illuminated windows stand beneath the open sky.", -1, ObservationOnly: true),
        // 301 sinin3: native base image reviewed; no inferred actor or item state.
        new(301, 0, 0, 16, "A circular stone passage narrows toward a round opening. Thick masonry rings and rough rock frame the entrance.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 304 sininb31: native base image reviewed; no inferred actor or item state.
        new(304, 0, 0, 16, "Tall bookcases surround a crowded worktable beneath hanging lamps. Thick pipes, cylindrical machinery and coiled cables fill the edges of the room.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 306 sininb33: native base image reviewed; no inferred actor or item state.
        new(306, -1, -1, -1, "Tall bookcases surround a crowded worktable beneath hanging lamps. Thick pipes, cylindrical machinery and coiled cables fill the edges of the room.", -1, ObservationOnly: true),
        // 307 sininb41: native base image reviewed; no inferred actor or item state.
        new(307, 0, 0, 16, "A low stone passage leads through an arched opening. Tall bookcases crowd the walls, and a rough stone floor slopes toward the doorway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 310 sininb52: native base image reviewed; no inferred actor or item state.
        new(310, 0, 0, 4, "A central worktable stands among tall shelves and heavy pipes. Narrow platforms and scattered furniture surround the cramped lower work area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 320 nvdun31: native base image reviewed; no inferred actor or item state.
        new(320, 0, 0, 15, "Branching formations rise from an uneven rocky floor. A smooth, curved wall fills the background beyond the jagged foreground rocks.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 321 nvdun4: native base image reviewed; no inferred actor or item state.
        new(321, 0, 0, 32, "Water spills down a rugged cavern wall. Hanging vines and uneven rock columns surround deep gaps in the cave.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 328 elm_wa: native base image reviewed; no inferred actor or item state.
        new(328, 6, 0, 14, "A narrow counter faces glass display cases and tall wall cabinets. Lamps light the paneled room, with a small table near the entrance.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 329 elm_i: native base image reviewed; no inferred actor or item state.
        new(329, 3, 0, 14, "Wooden counters and shelves line a small shop. A railing separates the entrance floor from displays of bottles, boxes and small containers.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 330 elmpb: native base image reviewed; no inferred actor or item state.
        new(330, 4, 0, 14, "A long bar curves around a row of stools. Bottles line the shelves behind it, with small tables set against the paneled walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 331 elminn_1: native base image reviewed; no inferred actor or item state.
        new(331, 2, 0, 0, "A broad reception counter stands beside a staircase. Upholstered seating, framed pictures and a tall clock furnish the tiled lobby.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 332 elminn_2: native base image reviewed; no inferred actor or item state.
        new(332, 4, 0, 0, "A wooden landing overlooks a bedroom with three single beds. Cabinets and small lamps line the walls, and a staircase descends beside the landing.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 333 elmin1_1: native base image reviewed; no inferred actor or item state.
        new(333, 2, 0, 14, "A square dining table sits between a kitchen counter and a wooden staircase. Cabinets, shelves and framed pictures line the room.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 334 elmin1_2: native base image reviewed; no inferred actor or item state.
        new(334, 2, 0, 14, "Two single beds stand along one side of a wooden room. Tall tanks, wall cabinets and exposed pipework occupy the opposite side.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 335 elm: native base image reviewed; no inferred actor or item state.
        new(335, 7, 0, 14, "Timbered houses surround a paved square inside high stone walls. A tall cylindrical tower stands in the center, and broad stairs rise to an arched passage.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 336 elmin2_1: native base image reviewed; no inferred actor or item state.
        new(336, 3, 0, 14, "A dining table and chairs stand near a stove in a small timbered room. A wooden staircase rises along the wall beside cabinets and hanging utensils.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 337 elmin2_2: native base image reviewed; no inferred actor or item state.
        new(337, 3, 0, 14, "Two single beds share a small room beneath exposed beams. Cabinets and wall lamps stand behind a railing that borders the landing.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 338 elmin3_1: native base image reviewed; no inferred actor or item state.
        new(338, 2, 0, 14, "A tall machinery alcove adjoins a small dining room. Cabinets, a wooden staircase and exposed pipes crowd the walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 339 elmin3_2: native base image reviewed; no inferred actor or item state.
        new(339, 2, 0, 14, "Two single beds stand beside a wooden landing. Wall cabinets, lamps and exposed pipes line the room beyond its railing.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 340 elmtow: native base image reviewed; no inferred actor or item state.
        new(340, 3, 0, 14, "A tall, narrow chamber has bare stone walls and a wooden floor. A steep ladder rises along the back wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 341 elmin4_1: native base image reviewed; no inferred actor or item state.
        new(341, 3, 0, 14, "A dining table sits below shelves of bottles and dishes. A wooden staircase climbs beside a tall clock and exposed wall pipes.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 342 elmin4_2: native base image reviewed; no inferred actor or item state.
        new(342, 1, 0, 14, "Two single beds and a long rug furnish an upstairs room. A railing borders the landing beside wall cabinets and framed pictures.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 343 farm: native base image reviewed; no inferred actor or item state.
        new(343, 0, 0, 2, "A farmhouse, cylindrical silo and curved-roof barn stand beyond a wooden fence. Low wooded hills rise behind the open pasture.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 344 frmin: native base image reviewed; no inferred actor or item state.
        new(344, 0, 0, 2, "A kitchen and dining table fill the main farmhouse room. A separate sleeping area contains three single beds beneath small windows.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 345 frcyo: native base image reviewed; no inferred actor or item state.
        new(345, 0, 0, 2, "Two rows of fenced stalls face a long central aisle inside a high barn. Small windows sit above the wooden partitions.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 348 sichi: native base image reviewed; no inferred actor or item state.
        new(348, 0, 0, 0, "A long serpent is suspended against a broken tree beside a dirt path. High rocky banks and dense undergrowth enclose the clearing.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 349 psdun_1: native base image reviewed; no inferred actor or item state.
        new(349, 0, 0, 4, "Water falls through a deep rocky cleft. Narrow ledges and broken stone platforms cling to the walls beneath hanging roots.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 350 psdun_2: native base image reviewed; no inferred actor or item state.
        new(350, 0, 0, 4, "Rough stone walkways wind between jagged cavern walls. Narrow openings connect irregular ledges above dark gaps.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 351 psdun_3: native base image reviewed; no inferred actor or item state.
        new(351, 0, 0, 4, "A staircase curls around a tall rock pillar inside a deep cavern. Narrow stone ledges connect it to the surrounding walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 352 psdun_4: native base image reviewed; no inferred actor or item state.
        new(352, 2, 0, 4, "Broad, uneven ledges jut from a cavern wall above a deep drop. A smaller shelf lies below the central platform.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 353 condor1: native base image reviewed; no inferred actor or item state.
        new(353, 0, 0, 0, "A rough mountain trail climbs between huge boulders beside an open cliff. Sparse shrubs cling to the rocky slope.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 354 condor2: native base image reviewed; no inferred actor or item state.
        new(354, 0, 0, 0, "A narrow passage curves through a bare rock tunnel. Low stone ledges border the rough floor beneath the uneven ceiling.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 355 convil_1: native base image reviewed; no inferred actor or item state.
        new(355, 0, 0, 0, "Small rooms occupy several levels of a rocky cavern. Wooden platforms hold beds, shelves and tables, with ladders linking the living spaces.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 356 convil_2: native base image reviewed; no inferred actor or item state.
        new(356, 0, 0, 0, "A simple timber room has a broad window overlooking the sky. A low table and boxes sit beside a woven floor covering.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 358 convil_4: native base image reviewed; no inferred actor or item state.
        new(358, 0, 0, 0, "A broad rocky ledge sits below a large industrial tower. The mountaintop drops away beneath an open sky.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 359 junon: native base image reviewed; no inferred actor or item state.
        new(359, 0, 0, 62, "A wide paved passage runs beneath a low metal ceiling. Wall lights line the tunnel, which opens toward the surrounding industrial buildings.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 360 junonr1: native base image reviewed; no inferred actor or item state.
        new(360, 4, 0, 0, "A broad roadway follows a long row of tall, windowed buildings. Hanging banners, ground-level arches and large overhead pipes dominate the street.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 361 junonr2: native base image reviewed; no inferred actor or item state.
        new(361, 4, 0, 0, "Huge diagonal girders cross above a paved platform. Narrow railings and heavy pipes follow the metal structure around an open shaft.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 362 junonr3: native base image reviewed; no inferred actor or item state.
        new(362, 1, 0, 0, "A paved platform runs beside an industrial wall covered with broad panels and tall pipes. A striped barrier borders the near edge.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 363 junonr4: native base image reviewed; no inferred actor or item state.
        new(363, 0, 0, 56, "Repeated arched recesses line a broad building frontage. Lamps hang above the pavement, with banners descending between the upper windows.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 364 jun_wa: native base image reviewed; no inferred actor or item state.
        new(364, 0, 0, 0, "An oval glass display occupies the center of a metal-walled shop. Cabinets, tall display stands and exposed pipes line the perimeter.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 365 jun_i1: native base image reviewed; no inferred actor or item state.
        new(365, 0, 0, 0, "A wide shop counter faces shelves of small containers. Heavy overhead pipes, wall panels and short steps surround the tiled floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 366 jun_m: native base image reviewed; no inferred actor or item state.
        new(366, 0, 0, 0, "Counters and display trays divide a small tiled shop. Round vents, broad pipes and shelves of small containers cover the back wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 367 junmin1: native base image reviewed; no inferred actor or item state.
        new(367, 0, 0, 0, "A dining table stands in a compact wooden room below wall cabinets and framed pictures. An adjoining sleeping area and stairway lead off the main floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 368 junmin2: native base image reviewed; no inferred actor or item state.
        new(368, 1, 0, 0, "A small room contains a worktable and a bank of tall lockers. A wooden bed frame occupies the adjoining sleeping area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 369 junmin3: native base image reviewed; no inferred actor or item state.
        new(369, 1, 0, 0, "Two beds fill the upper room, with small desks and cabinets in the adjoining spaces. A staircase descends beside the partitioned living area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 370 junonl1: native base image reviewed; no inferred actor or item state.
        new(370, 7, 0, 28, "A wide street slopes beside a long building covered with windows and hanging banners. Bright doorways open onto the pavement beneath large arches.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 371 junonl2: native base image reviewed; no inferred actor or item state.
        new(371, 4, 0, 0, "A paved platform sits among enormous diagonal girders and vertical metal walls. Railings follow its exposed edge above a deep industrial shaft.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 372 junonl3: native base image reviewed; no inferred actor or item state.
        new(372, 1, 0, 0, "Broad metal panels and a small service door face a paved platform. A striped railing borders the open edge.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 373 jun_w: native base image reviewed; no inferred actor or item state.
        new(373, 1, 0, 0, "Tall display racks and metal counters fill a compact shop. Pipes and narrow platforms crowd the walls around the central floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 374 jun_a: native base image reviewed; no inferred actor or item state.
        new(374, 0, 0, 0, "A large shop counter fills a room with wooden flooring and broad wall displays. Metal railings and short stairs border the entrance.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 375 jun_i2: native base image reviewed; no inferred actor or item state.
        new(375, 0, 0, 0, "Counter partitions divide a tiled shop lined with shelves and cabinets. A short stairway rises from the entrance beside the display area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 376 juninn: native base image reviewed; no inferred actor or item state.
        new(376, 3, 0, 0, "A long room contains several beds beside small cabinets and wall fittings. A narrow reception counter and stairway occupy the entrance area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 377 junpb_1: native base image reviewed; no inferred actor or item state.
        new(377, 1, 0, 10, "Two round tables stand near a long counter inside a compact diner. Large ducts, wall signs and raised seating line the back.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 378 junpb_2: native base image reviewed; no inferred actor or item state.
        new(378, 1, 0, 0, "Heavy pipes and machinery surround small round tables on a tiled floor. A tall ladder rises against the rear wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 379 junpb_3: native base image reviewed; no inferred actor or item state.
        new(379, 1, 0, 10, "A long bar and rows of bottles overlook round tables on a patterned floor. A staircase enters the room beside broad wall partitions.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 380 junmin4: native base image reviewed; no inferred actor or item state.
        new(380, 1, 0, 0, "A large industrial machine fills the lower room, bordered by railings and striped panels. Beds and lockers occupy the separate room behind it.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 381 junmin5: native base image reviewed; no inferred actor or item state.
        new(381, 0, 0, 0, "A narrow room sits below a large overhead pipe. A small table, stools and cabinets fill the floor beside an adjoining sleeping area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 382 jundoc1a: native base image reviewed; no inferred actor or item state.
        new(382, 2, 0, 0, "A large ship has an open loading ramp facing the dock. Railings surround the upper deck, and a small doorway opens in the nearby wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 383 jundoc1b: native base image reviewed; no inferred actor or item state.
        new(383, 2, 0, 0, "A dock runs beside the water beneath a massive industrial wall. Exposed pipes follow the wall to a small doorway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 384 junair: native base image reviewed; no inferred actor or item state.
        new(384, 0, 0, 124, "A broad landing pad lies below a raised deck lined with lights. Stairs and railings connect the paved level to the surrounding platforms.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 386 junin1: native base image reviewed; no inferred actor or item state.
        new(386, 1, 0, 0, "Arrows mark the floor of a metal corridor bending around a corner. Large panels and exposed ducts line the walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 387 junin1a: native base image reviewed; no inferred actor or item state.
        new(387, 1, 0, 7, "Tall lockers and stacked boxes fill a small metal room. A narrow work surface and wall panels occupy the opposite corner.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 388 junele1: native base image reviewed; no inferred actor or item state.
        new(388, 2, 0, 0, "A metal lift cabin has a grated floor, broad doors and wall-mounted panels. Striped markings border the doorways.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 389 junin2: native base image reviewed; no inferred actor or item state.
        new(389, 0, 0, 141, "A wide metal corridor runs past heavy doors and wall pipes. Short side passages branch off the central walkway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 390 junin3: native base image reviewed; no inferred actor or item state.
        new(390, 1, 0, 0, "A long, straight corridor has metal walls, repeated lights and floor markings. Heavy door frames stand at either end.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 391 junele2: native base image reviewed; no inferred actor or item state.
        new(391, 1, 0, 11, "A square lift platform hangs inside a deep cylindrical shaft. Heavy pipes and curved walls surround its striped edges.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 392 junin4: native base image reviewed; no inferred actor or item state.
        new(392, 1, 0, 14, "A metal corridor bends between broad doorway frames. Large wall ducts and striped thresholds border the smooth central floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 393 junin5: native base image reviewed; no inferred actor or item state.
        new(393, 1, 0, 0, "Wide metal stairs connect three landings inside a long enclosed passage. Pipes and heavy door frames line the walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 394 junin6: native base image reviewed; no inferred actor or item state.
        new(394, 1, 0, 14, "A long flight of metal stairs joins two narrow landings. Heavy wall panels and exposed pipes enclose the passage.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 395 junin7: native base image reviewed; no inferred actor or item state.
        new(395, 2, 0, 0, "A striped lift platform sits inside a cylindrical shaft beneath a large caution sign. Thick pipes follow the curved walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 396 junbin1: native base image reviewed; no inferred actor or item state.
        new(396, 0, 0, 0, "A central display stands on a round floor inset in a high metal hall. Stairs, benches and broad wall signs surround it.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 397 junbin12: native base image reviewed; no inferred actor or item state.
        new(397, 0, 0, 14, "An open metal landing follows railings around a deep interior opening. A heavy doorway stands in the far wall beside exposed pipes.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 398 junbin21: native base image reviewed; no inferred actor or item state.
        new(398, 0, 0, 52, "Rows of chairs face a raised desk beneath a broad curtained opening. Large wall panels and pipes frame the long meeting room.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 399 junbin22: native base image reviewed; no inferred actor or item state.
        new(399, 0, 0, 0, "Tall windows overlook the open sea beneath sweeping curtains. Heavy decorative frames and a raised interior platform border the glass.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 400 junbin3: native base image reviewed; no inferred actor or item state.
        new(400, 0, 0, 0, "A raised table stands under a large circular lamp in a metal-walled room. Cabinets, wall instruments and broad windows surround the floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 401 junbin4: native base image reviewed; no inferred actor or item state.
        new(401, 0, 0, 35, "Rows of chairs face a long raised counter in a tall room. Wall panels and a large screen stand above the front.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 402 junbin5: native base image reviewed; no inferred actor or item state.
        new(402, 0, 0, 30, "A single chair stands on a raised base in a cramped metal chamber. Large pipes and wall-mounted equipment surround the tiled floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 404 junsbd1: native base image reviewed; no inferred actor or item state.
        new(404, 2, 0, 0, "A submarine rests beside narrow dock platforms in a large industrial bay. Catwalks, cranes and exposed pipes surround the berth.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 405 subin_1a: native base image reviewed; no inferred actor or item state.
        new(405, -1, -1, -1, "A large display faces the central seat in a crowded control room. Instrument consoles and gauges line both sides beneath exposed ceiling pipes.", -1, ObservationOnly: true),
        // 406 subin_1b: native base image reviewed; no inferred actor or item state.
        new(406, 7, 0, 0, "A large display faces the central seat in a crowded control room. Instrument consoles and gauges line both sides beneath exposed ceiling pipes.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 407 subin_2a: native base image reviewed; no inferred actor or item state.
        new(407, 4, 0, 60, "A narrow steel passage runs between tall racks and wall panels. Large pipes cross overhead, with short steps leading toward a heavy doorway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 408 subin_2b: native base image reviewed; no inferred actor or item state.
        new(408, 4, 0, 142, "A narrow steel passage runs between tall racks and wall panels. Large pipes cross overhead, with short steps leading toward a heavy doorway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 409 subin_3: native base image reviewed; no inferred actor or item state.
        new(409, 1, 0, 0, "Large cylindrical machinery crowds a grated engine room. Exposed pipes, ladders and broad circular openings border a narrow raised walkway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 411 junone2: native base image reviewed; no inferred actor or item state.
        new(411, 0, 0, 0, "An enormous cannon stretches above a paved platform beside the sea. Heavy support structures and broad metal barriers surround its base.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 412 junone3: native base image reviewed; no inferred actor or item state.
        new(412, 0, 0, 22, "Huge pipes and steel housings fill a tall industrial structure. Broad wall panels and narrow platforms surround the massive central machinery.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 413 junone4: native base image reviewed; no inferred actor or item state.
        new(413, 0, 0, 2, "A massive gun barrel extends over the open sea. Thick clamps and broad metal housings fill the foreground beneath the distant horizon.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 414 junone5: native base image reviewed; no inferred actor or item state.
        new(414, 0, 0, 2, "A massive metal cylinder rises beside a narrow industrial platform. Ribbed bands, attached pipes and heavy structural panels cover its sides.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 415 junone6: native base image reviewed; no inferred actor or item state.
        new(415, 0, 0, 16, "Heavy metal braces span a broad platform on top of the machinery. The sea stretches beyond the exposed deck.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 416 junone7: native base image reviewed; no inferred actor or item state.
        new(416, 0, 0, 2, "A broad, rounded metal surface fills the foreground above the sea. Open water and a distant horizon stretch beyond it.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 417 spgate: native base image reviewed; no inferred actor or item state.
        new(417, 4, 0, 0, "A narrow metal passage runs between heavy pipes and machinery. Striped doorway frames and overhead cables border the grated floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 418 spipe_1: native base image reviewed; no inferred actor or item state.
        new(418, 4, 0, 0, "A glass tunnel curves through the water beneath repeated metal arches. A paved central path follows its transparent walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 419 spipe_2: native base image reviewed; no inferred actor or item state.
        new(419, 4, 0, 0, "A straight glass passage leads toward a large industrial entrance beneath the water. Broad metal arches repeat over the tiled path.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 420 semkin_1: native base image reviewed; no inferred actor or item state.
        new(420, 5, 0, 0, "Tall glass cylinders line the back of a small industrial chamber. Control panels and ladders surround a circular inset in the raised floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 421 semkin_2: native base image reviewed; no inferred actor or item state.
        new(421, 0, 0, 0, "A long raised walkway crosses an industrial bay above submarine berths. Hanging machinery, thick pipes and broad platforms fill the walls behind it.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 422 semkin_8: native base image reviewed; no inferred actor or item state.
        new(422, 3, 0, 0, "A short industrial passage leads toward a large striped doorway. Heavy pipes and wall machinery crowd the narrow floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 423 semkin_3: native base image reviewed; no inferred actor or item state.
        new(423, 2, 0, 0, "Broad metal platforms sit among tall pipes and vertical machinery. Grated floor sections and low partitions divide the walkway.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 424 semkin_4: native base image reviewed; no inferred actor or item state.
        new(424, 5, 0, 0, "Thin metal bridges cross a deep shaft around a tall cylindrical machine. Heavy pipes and suspended cables fill the surrounding walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 425 semkin_5: native base image reviewed; no inferred actor or item state.
        new(425, 3, 0, 0, "A submarine fills a narrow berth below hanging machinery. Railings and small gangways connect the dock to the surrounding industrial platforms.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 426 semkin_6: native base image reviewed; no inferred actor or item state.
        new(426, 1, 0, 0, "Long dock platforms stretch across an industrial bay beneath hanging machinery. Narrow bridges and railings link the raised walkways above the water.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 427 semkin_7: native base image reviewed; no inferred actor or item state.
        new(427, 2, 0, 0, "A submarine fills a narrow berth below hanging machinery. Railings and small gangways connect the dock to the surrounding industrial platforms.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 428 ujunon1: native base image reviewed; no inferred actor or item state.
        new(428, 5, 0, 0, "Small houses cluster below high rocky walls. Stairs and rough paths wind between the buildings toward the water below.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 429 ujunon2: native base image reviewed; no inferred actor or item state.
        new(429, 2, 0, 0, "A tall lattice tower stands beside a rough waterfront. Heavy supports and narrow platforms extend toward the water.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 430 ujunon3: native base image reviewed; no inferred actor or item state.
        new(430, -1, -1, -1, "Metal platforms and lattice supports cross above the water beside a tall tower. Narrow walkways follow the beams along the waterfront.", -1, ObservationOnly: true),
        // 431 prisila: native base image reviewed; no inferred actor or item state.
        new(431, 3, 0, 0, "A bed, cabinets and a small dining table furnish a compact room. Wall pictures, a clock and hanging curtains line the walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 432 ujun_w: native base image reviewed; no inferred actor or item state.
        new(432, 0, 0, 0, "A long wooden counter borders an open shop floor. Boxes, tall cabinets and wall displays crowd the room behind it.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 433 jumin: native base image reviewed; no inferred actor or item state.
        new(433, 1, 0, 0, "Three single beds line one side of a stone-floored room. A large rug, cupboards and a fireplace furnish the adjoining living area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 434 ujunon4: native base image reviewed; no inferred actor or item state.
        new(434, 1, 0, 0, "Rough stone ground slopes toward a short staircase cut into a rocky wall. Small ledges and scattered rocks border the approach.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 435 ujunon5: native base image reviewed; no inferred actor or item state.
        new(435, -1, -1, -1, "Rough stone ground slopes toward a short staircase cut into a rocky wall. Small ledges and scattered rocks border the approach.", -1, ObservationOnly: true),
        // 436 ship_1: native base image reviewed; no inferred actor or item state.
        new(436, 2, 0, 60, "A ship deck holds broad gratings, stacked cargo and a striped raised platform. Railings and tall metal supports border the working area.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 437 ship_2: native base image reviewed; no inferred actor or item state.
        new(437, 0, 0, 46, "Broad windows rise above a narrow ship deck beside a striped grating. Railings, a ladder and a life ring line the exterior wall.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 438 shpin_22: native base image reviewed; no inferred actor or item state.
        new(438, -1, -1, -1, "Metal catwalks cross a broad cargo compartment. Railings, large pipes, and heavy doors surround an open floor beneath the ship's steel framework.", -1, ObservationOnly: true),
        // 439 shpin_2: native base image reviewed; no inferred actor or item state.
        new(439, 1, 0, 20, "Crates and cargo fill a steel compartment crossed by elevated catwalks. Broad pipes run along the floor, with railings and heavy doors around the edges.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 440 shpin_3: native base image reviewed; no inferred actor or item state.
        new(440, 0, 0, 60, "Grated walkways surround a lower machinery floor. Large pipes, tanks, and metal braces fill the ship's narrow compartment.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 441 del1: native base image reviewed; no inferred actor or item state.
        new(441, 2, 0, 80, "A broad waterfront quay runs beside a large passenger ship. A red seaplane floats near the stone embankment, with a lighthouse at the far end.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 442 del12: native base image reviewed; no inferred actor or item state.
        new(442, -1, -1, -1, "A broad waterfront quay runs beside a large passenger ship. A red seaplane floats near the stone embankment, with a lighthouse at the far end.", -1, ObservationOnly: true),
        // 443 del2: native base image reviewed; no inferred actor or item state.
        new(443, 1, 0, 0, "A paved street winds between low buildings with tiled roofs. A stone archway opens on one side, while an inn and shopfronts line the other.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 444 delinn: native base image reviewed; no inferred actor or item state.
        new(444, 2, 0, 0, "A staircase divides an inn into a lower common room and an upper sleeping loft. Beds, rugs, tables, and wooden railings fill the space.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 445 delpb: native base image reviewed; no inferred actor or item state.
        new(445, 0, 0, 0, "A curved wooden bar faces rows of stools and small tables. Surfboards and hanging decorations cover the walls beneath the sloping roof.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 446 delmin1: native base image reviewed; no inferred actor or item state.
        new(446, 5, 0, 6, "Beds stand on a raised wooden platform above a crowded living area. Slatted partitions, stairs, tables, and household furnishings divide the room.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 447 delmin12: native base image reviewed; no inferred actor or item state.
        new(447, 0, 0, 0, "Stone steps descend into a compact cellar filled with crates and barrels. Lanterns hang along rough masonry walls beside a small worktable.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 448 delmin2: native base image reviewed; no inferred actor or item state.
        new(448, 4, 0, 0, "Two beds occupy an upper wooden loft. Below, a dining table and household furnishings sit behind railings, with stairs linking the levels.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 449 del3: native base image reviewed; no inferred actor or item state.
        new(449, 1, 0, 0, "Striped sun umbrellas and loungers line a sandy beach beside shallow water. Small boats rest along the shore near the beachfront buildings.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 450 ncorel: native base image reviewed; no inferred actor or item state.
        new(450, 1, 0, 0, "Makeshift huts and piles of scrap crowd a dusty hillside settlement. Rough paths weave between battered structures, beside a tilted Gold Saucer sign.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 451 ncorel2: native base image reviewed; no inferred actor or item state.
        new(451, 0, 0, 0, "Makeshift huts and piles of scrap crowd a dusty hillside settlement. Rough paths weave between battered structures, beside a tilted Gold Saucer sign.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 452 ncorel3: native base image reviewed; no inferred actor or item state.
        new(452, 1, 0, 0, "Makeshift huts and piles of scrap crowd a dusty hillside settlement. Rough paths weave between battered structures, beside a tilted Gold Saucer sign.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 453 ncoin1: native base image reviewed; no inferred actor or item state.
        new(453, 0, 0, 0, "Canvas walls enclose a cramped shelter with a low table and cooking pot. Containers, small appliances, and supplies stand against the sloping fabric.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 454 ncoin2: native base image reviewed; no inferred actor or item state.
        new(454, 0, 0, 0, "A small tent shelters a low cooking area beneath a hanging lantern. Boxes, cans, and household supplies crowd the floor and rear shelves.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 455 ncoin3: native base image reviewed; no inferred actor or item state.
        new(455, 0, 0, 0, "Rough rock and broken masonry enclose a modest room. Tables, jars, cooking equipment, and worn furnishings stand on an uneven floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 456 ncoinn: native base image reviewed; no inferred actor or item state.
        new(456, 0, 0, 0, "Several narrow beds occupy a room with cracked stone walls. Small tables and cupboards stand between the mattresses and broken sections of floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 457 ropest: native base image reviewed; no inferred actor or item state.
        new(457, 1, 0, 0, "A rail station stands on raised metal platforms beneath overhead cables. A train car sits beside stairs, scaffolded walkways, and tracks extending uphill.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 458 mtcrl_0: native base image reviewed; no inferred actor or item state.
        new(458, 1, 0, 0, "A narrow trail climbs a steep rocky hillside among trees and exposed roots. Small ledges and worn steps wind upward between the outcrops.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 459 mtcrl_1: native base image reviewed; no inferred actor or item state.
        new(459, 2, 0, 0, "A winding ledge crosses a steep rock face beneath a twisted leafless tree. Rough steps and narrow shelves continue between the mountain outcrops.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 460 mtcrl_2: native base image reviewed; no inferred actor or item state.
        new(460, 4, 0, 0, "A tall industrial tower rises from a circular complex within a rocky canyon. Pipes, metal platforms, and bridges connect the surrounding machinery.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 461 mtcrl_3: native base image reviewed; no inferred actor or item state.
        new(461, 0, 0, 0, "Railway tracks cross a narrow chasm on a metal bridge. An arched tunnel opens in the cliff beyond stairs and supporting struts.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 462 mtcrl_4: native base image reviewed; no inferred actor or item state.
        new(462, 2, 0, 0, "Railway tracks rise and dip across a broad wooden trestle. A dense framework of tall supports and crossed beams fills the space beneath them.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 463 mtcrl_5: native base image reviewed; no inferred actor or item state.
        new(463, 0, 0, 0, "A tall wooden trestle fills the view, with closely spaced beams braced into repeating crossed panels.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 464 mtcrl_6: native base image reviewed; no inferred actor or item state.
        new(464, 2, 0, 0, "Broken railway tracks wind among damaged brick supports beside a steep slope. Wooden platforms and an arched structure remain above the rubble.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 465 mtcrl_7: native base image reviewed; no inferred actor or item state.
        new(465, 0, 0, 0, "A rocky shelter holds a crowded workbench, crates, and barrels. Long metal beams lean against the wall beside tools and scattered supplies.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 466 mtcrl_8: native base image reviewed; no inferred actor or item state.
        new(466, 2, 0, 0, "Two wooden chests sit beside a large grassy nest tucked into a hollow. Heavy roots and rough rock surround the sheltered space.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 467 mtcrl_9: native base image reviewed; no inferred actor or item state.
        new(467, 0, 0, 0, "A narrow suspension bridge stretches across a deep mountain gorge. Planks sag between high rock walls beneath long supporting ropes.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 468 corel1: native base image reviewed; no inferred actor or item state.
        new(468, -1, -1, -1, "A rocky mountain ledge overlooks a forested valley. Layered ridges rise beyond the trees, with steep cliffs bordering the open view.", -1, ObservationOnly: true),
        // 469 corel2: native base image reviewed; no inferred actor or item state.
        new(469, -1, -1, -1, "Weathered wooden houses stand around a broad dirt clearing. Tracks crisscross the ground between porches, barrels, and a raised water tower.", -1, ObservationOnly: true),
        // 470 corel3: native base image reviewed; no inferred actor or item state.
        new(470, -1, -1, -1, "Buildings line a broad dirt clearing, with a raised water tower on the left and a larger two-story building at the far side.", -1, ObservationOnly: true),
        // 471 jail1: native base image reviewed; no inferred actor or item state.
        new(471, 0, 0, 4, "Large pipes run between battered industrial buildings across dusty, uneven ground. Fences, scrap metal, and a wrecked vehicle crowd the yard.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 472 jailin1: native base image reviewed; no inferred actor or item state.
        new(472, 0, 0, 4, "A ribbed skeleton lies across a circular metal floor, surrounded by heavy pipes, a grated hatch, and warning signs.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 473 jail2: native base image reviewed; no inferred actor or item state.
        new(473, 0, 0, 4, "A tall industrial building overlooks closely packed sheds and low rooftops. Pipes, fences, and scattered wreckage fill the dusty ground below.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 474 jailpb: native base image reviewed; no inferred actor or item state.
        new(474, 0, 0, 2, "A curved bar faces a line of stools inside a rough brick room. Small round tables stand beneath wall lamps and exposed pipes.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 475 jailin2: native base image reviewed; no inferred actor or item state.
        new(475, 0, 0, 12, "A worn couch faces a low table in a small wooden room. Shelves, cabinets, and scattered household items surround a patterned rug.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 476 jailin3: native base image reviewed; no inferred actor or item state.
        new(476, -1, -1, -1, "A damaged metal room holds narrow beds and machinery. Cracked walls, exposed tanks, and scattered debris surround a broken doorway.", -1, ObservationOnly: true),
        // 477 jailin4: native base image reviewed; no inferred actor or item state.
        new(477, 0, 0, 0, "Heavy pipes and tanks stand against a grated wall in a small industrial room. A crate and scattered debris lie on the metal floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 478 jail3: native base image reviewed; no inferred actor or item state.
        new(478, 0, 0, 4, "A narrow metal footbridge crosses a deep crack in a dry plain. Its supports stretch between rocky edges toward the open desert.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 479 jail4: native base image reviewed; no inferred actor or item state.
        new(479, 0, 0, 6, "Wrecked vehicles are piled along a dusty slope. Twisted pipes, wheels, and broken machinery crowd the ground beside the path.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 480 dyne: native base image reviewed; no inferred actor or item state.
        new(480, -1, -1, -1, "A rocky clearing ends at a steep drop. Tires and scrap lie near a small wooden structure, with distant hills beyond the edge.", -1, ObservationOnly: true),
        // 481 desert1: native base image reviewed; no inferred actor or item state.
        new(481, -1, -1, -1, "An open desert plain stretches toward distant mountains. Scattered stones and sparse scrub break up the dry, uneven ground.", -1, ObservationOnly: true),
        // 482 desert2: native base image reviewed; no inferred actor or item state.
        new(482, 0, 0, 9, "A broad fissure cuts across the foreground of a dry plain. Broken rocky edges overlook the desert and distant mountain ridges.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 483 corelin: native base image reviewed; no inferred actor or item state.
        new(483, -1, -1, -1, "A patterned rug fills a compact wooden living room. A couch faces shelves of books, jars, and a clock along the back wall.", -1, ObservationOnly: true),
        // 485 astage_b: native base image reviewed; no inferred actor or item state.
        new(485, -1, -1, -1, "A wooden stage stands before a painted village backdrop bearing the Gold Saucer name. Draped curtains frame the sides.", -1, ObservationOnly: true),
        // 493 ghotin_4: native base image reviewed; no inferred actor or item state.
        new(493, 0, 0, 19, "A winding staircase rises through a dim lobby to an upper balcony. Skull ornaments flank dark doorways, while tables and chairs sit below.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 494 ghotin_2: native base image reviewed; no inferred actor or item state.
        new(494, 0, 0, 35, "Coffin-shaped beds stand beneath tall candles and wall ornaments. A round table and unusual furnishings occupy the carpeted bedroom.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 496 gldst: native base image reviewed; no inferred actor or item state.
        new(496, 0, 0, 69, "Curving ramps and bridges weave between oversized fantasy structures. Large entrance arches and signs mark branching paths through the amusement park.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 497 gldgate: native base image reviewed; no inferred actor or item state.
        new(497, 0, 0, 42, "Seven round tunnel entrances ring a circular platform decorated with a cartoon bird. A broad illuminated walkway meets the platform from the right.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 498 gldinfo: native base image reviewed; no inferred actor or item state.
        new(498, -1, -1, -1, "A park map shows branching routes to Chocobo, Event, Ghost, Round, Speed, Battle, and Wonder squares.", -1, ObservationOnly: true),
        // 499 coloss: native base image reviewed; no inferred actor or item state.
        new(499, 0, 0, 26, "A broad approach leads toward a large arena entrance. Flared towers, illuminated signs, and decorative columns rise beside the central staircase.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 500 coloin1: native base image reviewed; no inferred actor or item state.
        new(500, 0, 0, 14, "A long carpet crosses a checkerboard lobby to steps beneath a skull emblem. A side archway leads into another room, with counters beside the entrance.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 501 coloin2: native base image reviewed; no inferred actor or item state.
        new(501, 0, 0, 39, "A long carpet crosses a checkerboard lobby to steps beneath a skull emblem. A side archway leads into another room, with counters beside the entrance.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 502 clsin2_1: native base image reviewed; no inferred actor or item state.
        new(502, 0, 0, 14, "A circular fighting area sits on a raised stone platform surrounded by posts and ropes. Broad stairs lead down to the wooden floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 503 clsin2_2: native base image reviewed; no inferred actor or item state.
        new(503, 0, 0, 14, "A portrait hangs over a broad display pedestal. Tall ornaments line the walls, and a curved platform projects above the checkerboard floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 504 clsin2_3: native base image reviewed; no inferred actor or item state.
        new(504, -1, -1, -1, "A circular opening occupies the floor of a compact chamber. Carved wall panels surround it, and a large metal mechanism stands beside the opening.", -1, ObservationOnly: true),
        // 508 mogu_1: native base image reviewed; no inferred actor or item state.
        new(508, -1, -1, -1, "A giant mushroom caps a small tree house surrounded by flowers and smaller mushrooms. Fruit hangs near the doorway beneath a rocky woodland wall.", -1, ObservationOnly: true),
        // 510 chorace2: native base image reviewed; no inferred actor or item state.
        new(510, 0, 0, 66, "A broad illuminated staircase climbs toward a large Chocobo Racing sign. Tubular entrances branch from both sides beneath cables and decorative lights.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 512 crcin_2: native base image reviewed; no inferred actor or item state.
        new(512, 0, 0, 159, "A curved waiting room contains a desk, computer terminals, and a large Chocobo Racing poster. Tall machines line the back wall beside a circular floor inset.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 513 gldelev: native base image reviewed; no inferred actor or item state.
        new(513, -1, -1, -1, "A tall elevator shaft drops between metal walls and narrow rails. Stacked platforms and small signal lamps run along its sides.", -1, ObservationOnly: true),
        // 527 cosmo: native base image reviewed; no inferred actor or item state.
        new(527, 2, 0, 0, "Buildings and walkways climb a cluster of tall rock pillars. Ladders connect the terraced settlement to structures on the highest outcrop.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 528 cosmo2: native base image reviewed; no inferred actor or item state.
        new(528, -1, -1, -1, "Terraced buildings cluster against tall rock faces. Long ladders and raised wooden paths connect levels, with a domed structure perched above the settlement.", -1, ObservationOnly: true),
        // 534 cosin5: native base image reviewed; no inferred actor or item state.
        new(534, 1, 0, 0, "A tall, narrow rock column fills the view beneath a small timber structure. Rough ledges and exposed wooden supports frame the upper section.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 542 bugin1b: native base image reviewed; no inferred actor or item state.
        new(542, 0, 0, 0, "A star field surrounds a model of the solar system. A ringed planet hangs above smaller spheres, while orbital rings cross a curved grid.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 543 bugin1c: native base image reviewed; no inferred actor or item state.
        new(543, 0, 0, 0, "A star field surrounds a model of the solar system. A ringed planet hangs above smaller spheres, while orbital rings cross a curved grid.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 545 bugin3: native base image reviewed; no inferred actor or item state.
        new(545, -1, -1, -1, "A large globe and a smaller moon float against a starry background. Curved grid lines sweep behind them through clouds of distant light.", -1, ObservationOnly: true),
        // 550 seto1: native base image reviewed; no inferred actor or item state.
        new(550, 0, 0, 0, "A weathered statue rests atop a tall rock pinnacle. Mist fills the gaps between layered ledges, with a moon hanging above.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 552 rckt3: native base image reviewed; no inferred actor or item state.
        new(552, -1, -1, -1, "A towering cylindrical structure rises behind fences and wooden houses. Metal stairs and scaffolding climb its sides, while a broad yard spreads below.", -1, ObservationOnly: true),
        // 572 yougan: native base image reviewed; no inferred actor or item state.
        new(572, 0, 0, 7, "A broad patch of uneven ground lies between low rocky outcrops. Grass breaks through the worn earth, with scattered stones around the edges.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 573 yougan2: native base image reviewed; no inferred actor or item state.
        new(573, 0, 0, 0, "A grassy slope stretches toward scattered low rocks. The ground is uneven and lightly worn, with open space between the outcrops.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 574 yougan3: native base image reviewed; no inferred actor or item state.
        new(574, 0, 0, 7, "A grassy slope stretches toward scattered low rocks. The ground is uneven and lightly worn, with open space between the outcrops.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 575 uta_wa: native base image reviewed; no inferred actor or item state.
        new(575, -1, -1, -1, "Long blades hang above a wooden shop counter. Weapon racks, wall displays, hanging signs, and a small rug crowd the open room.", -1, ObservationOnly: true),
        // 583 hideway1: native base image reviewed; no inferred actor or item state.
        new(583, 0, 0, 0, "A narrow wooden corridor is enclosed by tall railings. A metal-bound chest rests at the far end beneath the crossbars.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 584 hideway2: native base image reviewed; no inferred actor or item state.
        new(584, 0, 0, 0, "A narrow wooden corridor is enclosed by tall railings. A metal-bound chest rests at the far end beneath the crossbars.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 585 hideway3: native base image reviewed; no inferred actor or item state.
        new(585, 0, 0, 0, "A large metal-bound chest blocks the foreground of a narrow wooden passage. Tall railings line both sides, and the corridor continues behind it.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 597 datiao_6: native base image reviewed; no inferred actor or item state.
        new(597, 0, 0, 0, "A huge carved stone face is set into a rugged cliff. Circular emblems decorate its headdress, and broad stone ledges curve around the base.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 612 kuro_82: native base image reviewed; no inferred actor or item state.
        new(612, 2, 0, 8, "A long stone chamber is divided by carved pillars. Tall sheets of flame fill the spaces between them, and a stone pedestal stands at one end.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 613 kuro_9: native base image reviewed; no inferred actor or item state.
        new(613, -1, -1, -1, "A stone throne rests on a raised platform between carved columns. A brazier stands nearby, while flames rise behind the wall of pillars.", -1, ObservationOnly: true),
        // 615 kuro_11: native base image reviewed; no inferred actor or item state.
        new(615, 0, 0, 0, "A narrow, rough stone corridor passes through a rounded arch. The passage stretches away toward a brightly lit opening.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 628 sandun_1: native base image reviewed; no inferred actor or item state.
        new(628, 0, 0, 124, "A tall, narrow rocky shaft is crossed by small ledges and stairways, with bright pools of light glowing at different heights.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 631 losin1: native base image reviewed; no inferred actor or item state.
        new(631, -1, -1, -1, "A circular room descends around an open central shaft. Curving railings surround the tiers, and small illuminated panels line the rounded outer wall.", -1, ObservationOnly: true),
        // 639 loslake3: native base image reviewed; no inferred actor or item state.
        new(639, -1, -1, -1, "A round pool sits among broken stone columns and worn rocky terraces. Several narrow streams descend into the pool from above.", -1, ObservationOnly: true),
        // 642 white1: native base image reviewed; no inferred actor or item state.
        new(642, -1, -1, -1, "A narrow stone walkway leads to a circular altar surrounded by bare branching shapes. A large shell-like structure rises above the engraved platform.", -1, ObservationOnly: true),
        // 643 white2: native base image reviewed; no inferred actor or item state.
        new(643, -1, -1, -1, "A narrow walkway reaches a round stone platform ringed by bare branching shapes. A large shell-like canopy rests above its center.", -1, ObservationOnly: true),
        // 644 hekiga: native base image reviewed; no inferred actor or item state.
        new(644, -1, -1, -1, "An engraved circular altar fills the view, framed by twisting branches and curved stone surfaces. A large open shell rises over its center.", -1, ObservationOnly: true),
        // 645 whitein: native base image reviewed; no inferred actor or item state.
        new(645, 0, 0, 0, "Tall translucent spires surround a compact cluster of buildings on a circular platform. A long pale ribbon curves high above the towers.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 648 ancnt3: native base image reviewed; no inferred actor or item state.
        new(648, -1, -1, -1, "A narrow raised walkway runs between towering curved crystal-like structures. The view stretches upward toward a distant pool of light.", -1, ObservationOnly: true),
        // 663 hyou4: native base image reviewed; no inferred actor or item state.
        new(663, 0, 0, 0, "A snowy ledge curves around deep gaps in the rock. A long pale pointed formation rises from the edge of the path.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 665 hyou5_2: native base image reviewed; no inferred actor or item state.
        new(665, 0, 0, 0, "A broad body of water fills the view between snow-covered rocky banks. A jagged shelf of ice juts out from the foreground.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 666 hyou5_3: native base image reviewed; no inferred actor or item state.
        new(666, 0, 0, 0, "A low rocky shelter contains stacked crates and scattered equipment. Heavy supports brace the roof, and a bright opening lights the far end.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 670 move_s: native base image reviewed; no inferred actor or item state.
        new(670, 0, 0, 0, "Snow covers a broad, gently sloping stretch of ground. Dark patches of rock show through the pale surface.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 671 move_i: native base image reviewed; no inferred actor or item state.
        new(671, 0, 0, 0, "A snowy channel stretches between rough rock walls. The exposed stone slopes rise on both sides of the narrow white strip.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 672 move_f: native base image reviewed; no inferred actor or item state.
        new(672, 0, 0, 0, "Snow-laden evergreen trees stand beneath steep rocky slopes. A snowy clearing winds between the trees and the surrounding cliffs.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 673 move_r: native base image reviewed; no inferred actor or item state.
        new(673, 0, 0, 0, "A narrow snow-covered route runs between rugged rock faces. The pale strip cuts horizontally across the exposed mountainside.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 674 move_u: native base image reviewed; no inferred actor or item state.
        new(674, 0, 0, 0, "A steep snowy passage climbs between close rock walls. Scattered dark patches interrupt the snow along the narrow slope.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 675 move_d: native base image reviewed; no inferred actor or item state.
        new(675, 0, 0, 0, "A narrow snowy passage curves between tall, rough rock faces. Its pale surface descends through the gap between the cliffs.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 698 gaiin_6: native base image reviewed; no inferred actor or item state.
        new(698, 0, 0, 0, "Tall pointed crystal formations enclose a narrow passage. A brilliant glow shines at the far end between the crowded jagged walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 699 gaiin_7: native base image reviewed; no inferred actor or item state.
        new(699, 1, 0, 0, "Clusters of large pointed crystals surround a rough rocky chamber. A bright opening lies beyond the uneven ledges and stone floor.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 700 crater_1: native base image reviewed; no inferred actor or item state.
        new(700, 1, 0, 0, "A steep, uneven ridge of exposed rock rises diagonally across the view, with dark open space surrounding its narrow edge.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 707 trnad_52: native base image reviewed; no inferred actor or item state.
        new(707, 0, 0, 3, "A rough rocky hollow is framed by thick, twisting formations. The enclosing walls bend around a recessed space at the center.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 715 itown2: native base image reviewed; no inferred actor or item state.
        new(715, 0, 0, 0, "A cobbled street passes timber buildings and dense trees. Wooden railings border the walkway, and dark clouds hang over the town.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 723 life: native base image reviewed; no inferred actor or item state.
        new(723, 0, 0, 0, "A deep vertical shaft glows beneath a bright opening overhead. Dark fragments hang at different heights in the hazy space below.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 724 life2: native base image reviewed; no inferred actor or item state.
        new(724, 0, 0, 0, "Light pours down through a tall, hazy shaft. Several dark fragments are suspended beneath the bright opening above.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 725 zmind1: native base image reviewed; no inferred actor or item state.
        new(725, 0, 0, 0, "Round stone platforms float among scattered spheres and disconnected fragments of buildings. Narrow walkways link some of the layered platforms.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 726 zmind2: native base image reviewed; no inferred actor or item state.
        new(726, 0, 0, 0, "Large round stone platforms stand at different heights among floating spheres and broken building fragments. A raised walkway crosses the suspended structures.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 727 zmind3: native base image reviewed; no inferred actor or item state.
        new(727, 0, 0, 0, "Round stone platforms and narrow walkways hang amid scattered spheres. Tall cylindrical supports and fragments of buildings surround the disconnected levels.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 729 zcoal_2: native base image reviewed; no inferred actor or item state.
        new(729, 1, 0, 0, "A line of metal freight cars stretches across the view. Each open-topped car holds a mound of coal above its braced side panels.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 742 md_e1: native base image reviewed; no inferred actor or item state.
        new(742, -1, -1, -1, "A massive cylindrical industrial tower rises overhead, surrounded by thick pipes and stacked machinery. A fiery sky fills the space above it.", -1, ObservationOnly: true),
        // 744 las0_1: native base image reviewed; no inferred actor or item state.
        new(744, 4, 0, 2, "The airship hull curves past a large propeller. A painted woman with flowing hair decorates the metal panel above the machinery.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 751 las0_8: native base image reviewed; no inferred actor or item state.
        new(751, 4, 0, 0, "Broad rocky ledges project into a deep cavern. Long stalactites hang above an open chasm, and a narrow natural bridge extends from one shelf.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 756 las2_1: native base image reviewed; no inferred actor or item state.
        new(756, 3, 0, 0, "A deep, steep-sided rocky hollow drops toward a bright opening below. Rough ledges and pointed formations line the surrounding cavern walls.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 759 las2_4: native base image reviewed; no inferred actor or item state.
        new(759, 1, 0, 0, "A rounded rocky basin opens beneath a curtain of dense foliage. The uneven stone slopes converge around a bright opening at its center.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 767 las4_4: native base image reviewed; no inferred actor or item state.
        new(767, -1, -1, -1, "Flat rectangular slabs form a broken path suspended above dark gaps. Jagged mountain-like ridges rise behind the scattered platforms.", -1, ObservationOnly: true),
        // 768 lastmap: native base image reviewed; no inferred actor or item state.
        new(768, 4, 0, 0, "Irregular floating rocks surround a massive jagged formation. Several bright circular openings puncture its surface above a broad, uneven stone platform.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 771 hill2: native base image reviewed; no inferred actor or item state.
        new(771, 0, 0, 0, "A low, weathered hill stretches across the view. Exposed stone ridges and patches of grass cover its uneven slopes.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 772 bonevil2: native base image reviewed; no inferred actor or item state.
        new(772, 0, 0, 0, "Enormous bones and rib-like arches protrude from the earth around a narrow settlement. Wooden structures and pathways nestle among the towering remains.", FieldOpcodeAddressResolver.OpcodeMapNameIndex),
        // 773 junone22: native base image reviewed; no inferred actor or item state.
        new(773, -1, -1, -1, "A broad metal walkway runs beside a high railing. Angular buildings and tall structural walls rise beyond the open platform.", -1, ObservationOnly: true),
        // 774 rckt32: native base image reviewed; no inferred actor or item state.
        new(774, -1, -1, -1, "A fenced yard surrounds the base of a tall industrial tower. Heavy supports, pipes, and stairs crowd its cylindrical structure.", -1, ObservationOnly: true),
        // 777 las4_42: native base image reviewed; no inferred actor or item state.
        new(777, -1, -1, -1, "A broken path of rectangular stone slabs hangs over dark gaps. Jagged ridges stand beyond the separated platforms.", -1, ObservationOnly: true),
        // 779 md8_52: native base image reviewed; no inferred actor or item state.
        new(779, -1, -1, -1, "Tall brick buildings crowd beneath an immense steel framework. Lit windows and street lamps illuminate the walls, while heavy beams cross overhead.", -1, ObservationOnly: true),
        // 780 sininb34: native base image reviewed; no inferred actor or item state.
        new(780, -1, -1, -1, "Large cylindrical glass tanks stand along the stone wall of a laboratory. Thick pipes, gauges, and metal braces surround the illuminated vessels.", -1, ObservationOnly: true),
        // 781 mds7st33: native base image reviewed; no inferred actor or item state.
        new(781, -1, -1, -1, "A metal train stands beside a raised stone platform. Tall lamps illuminate its windows, and broad steps lead down from the platform.", -1, ObservationOnly: true),
        // 782 midgal: native base image reviewed; no inferred actor or item state.
        new(782, -1, -1, -1, "A rocky bluff fills the view, dotted with rough outcrops and dense patches of vegetation along its uneven slopes.", -1, ObservationOnly: true),
        // 783 sininb35: native base image reviewed; no inferred actor or item state.
        new(783, -1, -1, -1, "A crowded laboratory contains rows of cylindrical tanks, thick pipes, and an illuminated control table. Shelves and wall-mounted panels surround the machinery.", -1, ObservationOnly: true),
        // 784 nivgate4: native base image reviewed; no inferred actor or item state.
        new(784, -1, -1, -1, "A dirt road passes timber houses, barrels, and short white fences. Tall street lamps line the road between the closely spaced buildings.", -1, ObservationOnly: true),
        // 785 sininb36: native base image reviewed; no inferred actor or item state.
        new(785, -1, -1, -1, "Tall cylindrical glass tanks line a dim stone laboratory. Heavy pipes and braces surround the vessels, with smaller gauges and fittings packed between them.", -1, ObservationOnly: true),
        // 786 ztruck: native base image reviewed; no inferred actor or item state.
        new(786, -1, -1, -1, "An old pickup truck fills the side view. Its open cargo bed sits behind a rounded cab, with broad curved fenders over the wheels.", -1, ObservationOnly: true),
    ];
}
