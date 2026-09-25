using System.Text.Json.Nodes;
using Ff7.Accessibility.Reloaded;

namespace WholeGameStateAudit;

/// <summary>
/// Builds one field's inventory and its comparison with the shipping readers, and feeds
/// the archive-wide counters, flag graph and candidate list.
/// </summary>
internal sealed class FieldReport
{
    /// <summary>What a sighted player notices when an interaction runs.</summary>
    public static readonly HashSet<string> PerceivableKinds = new(StringComparer.Ordinal)
    {
        "Dialog", "Ask", "Menu", "ItemAdd", "ItemRemove", "MateriaAdd", "MateriaRemove", "GilAdd", "GilRemove",
        "MapJump", "Battle", "Minigame", "Movie", "GameOver", "PartyAdd", "PartyRemove", "PartySet", "MemberAvailability"
    };

    public static readonly HashSet<string> PickupKinds = new(StringComparer.Ordinal) { "ItemAdd", "MateriaAdd", "GilAdd" };

    /// <summary>Effects that change where the player can go or what they can use.</summary>
    public static readonly HashSet<string> AccessKinds = new(StringComparer.Ordinal)
    {
        "MapJump", "TriangleLock", "LineEnable", "TalkEnable", "Visibility", "GatewaysEnable", "Solid", "ModelLoad"
    };

    private static readonly HashSet<byte> NavigationOpcodes = [Opcodes.MAPJUMP, 0xC0, 0xC2, Opcodes.XYZI];

    private readonly AuditContext context;
    private readonly FieldAnalysis a;
    private readonly GlobalAudit global;
    private readonly string sha256;
    private readonly Dictionary<string, (IReadOnlyList<ClosureEffect> Effects, bool DepthLimited)> closures = new(StringComparer.Ordinal);
    private readonly HashSet<int> liveInstructions = [];
    private ShippingView? view;
    private FieldScriptNavigationReadResult? shipping;

    public FieldReport(AuditContext context, FieldAnalysis analysis, string sha256)
    {
        this.context = context;
        a = analysis;
        global = context.Global;
        this.sha256 = sha256;
        foreach (var entry in a.Entries.Where(entry => entry.Live))
        {
            liveInstructions.UnionWith(entry.Reach);
        }
    }

    private string Repro =>
        $"WholeGameStateAudit --archive {context.Label}=\"{context.Root}\" --out <T> --field {a.FieldId}";

    public JsonObject Build()
    {
        try
        {
            view = new ShippingView(a);
        }
        catch (Exception exception)
        {
            a.Issues.Add($"shipping ParseScriptGroups failed: {exception.GetType().Name}: {exception.Message}");
        }

        // Without an archive (synthetic fields) the repaired catalog is read from the same
        // bytes through the method its ReadField uses; the 0.6.8 catalog has none.
        shipping = context.Catalog?.ReadField(a.FieldId) ??
                   (ShippingReflection.IsRepairedCatalog
                       ? ShippingReflection.ReadFieldFromBytes(a.FieldId, a.FieldName, a.File.Bytes)
                       : null);
        var roots = a.Entries.Where(entry => entry.Live && entry.EngineTriggered).ToArray();
        foreach (var root in roots)
        {
            closures[root.Id] = a.Closure(root);
        }

        var json = new JsonObject
        {
            ["schema"] = "claude-field-audit/1",
            ["archive"] = context.Label,
            ["field"] = new JsonObject { ["id"] = a.FieldId, ["name"] = a.FieldName, ["sha256"] = sha256 },
            ["section"] = SectionJson(),
            ["decode"] = DecodeJson(),
            ["entities"] = EntitiesJson(),
            ["entries"] = EntriesJson(),
            ["interactions"] = InteractionsJson(roots),
            ["triggers"] = TriggersJson(),
            ["shipping"] = ShippingJson(roots),
            ["storyRows"] = StoryJson(),
            ["objectRows"] = ObjectRowsJson()
        };
        if (context.IncludeText)
        {
            json["dialogs"] = DialogsJson();
        }

        if (context.IncludeInstructions)
        {
            json["instructions"] = InstructionsJson();
        }

        ContributeFlags(roots);
        CountField(roots);
        return json;
    }

    private JsonObject SectionJson()
    {
        var s = a.Section;
        global.Count($"section.version.{s.Version:X4}");
        return new JsonObject
        {
            ["version"] = $"0x{s.Version:X4}",
            ["entities"] = s.EntityCount,
            ["models"] = s.ModelCount,
            ["slotsPerEntity"] = s.SlotCount,
            ["akao"] = s.AkaoCount,
            ["creator"] = s.Creator,
            ["name"] = s.Name,
            ["codeStart"] = s.CodeStart,
            ["codeEnd"] = s.CodeEnd,
            ["shippingCodeEnd"] = s.ShippingCodeEnd,
            ["stringOffset"] = s.StringOffset,
            ["dialogCount"] = s.DialogCount,
            ["issues"] = Array(s.Issues.Concat(a.Issues).Concat(a.File.Issues))
        };
    }

    private JsonObject DecodeJson()
    {
        var codeBytes = a.Section.CodeEnd - a.Section.CodeStart;
        var decoded = new bool[Math.Max(0, codeBytes)];
        var liveBytes = new bool[Math.Max(0, codeBytes)];
        foreach (var instruction in a.Flow.Instructions.Values)
        {
            for (var at = instruction.Offset; at < instruction.End; at++)
            {
                decoded[at - a.Section.CodeStart] = true;
                if (liveInstructions.Contains(instruction.Offset))
                {
                    liveBytes[at - a.Section.CodeStart] = true;
                }
            }
        }

        var anomalies = new JsonArray();
        foreach (var anomaly in a.Flow.Anomalies)
        {
            var isLive = liveInstructions.Contains(anomaly.Offset) ||
                         a.Entries.Any(entry => entry.Live && entry.Reach.Contains(anomaly.Offset));
            global.Count($"anomaly.{anomaly.Kind}{(isLive ? ".live" : ".dormant")}");
            anomalies.Add(new JsonObject
            {
                ["kind"] = anomaly.Kind.ToString(),
                ["at"] = anomaly.Offset,
                ["live"] = isLive,
                ["detail"] = anomaly.Detail,
                ["entries"] = Array(a.Entries.Where(entry => entry.Reach.Contains(anomaly.Offset)).Select(entry => entry.Id))
            });
            if (isLive)
            {
                AddCandidate("DecodeAnomaly", anomaly.Kind is AnomalyKind.UndefinedOpcode or AnomalyKind.Truncated or AnomalyKind.TargetOutsideCode or AnomalyKind.Overlap or AnomalyKind.RawMemoryOpcode ? 2 : 3,
                    $"{anomaly.Kind} at {anomaly.Offset}: {anomaly.Detail}",
                    new JsonObject
                    {
                        ["at"] = anomaly.Offset,
                        ["bytes"] = Bytes(anomaly.Offset, 16),
                        ["entries"] = Array(a.Entries.Where(entry => entry.Live && entry.Reach.Contains(anomaly.Offset)).Select(entry => entry.Id))
                    });
            }
        }

        global.Count("decode.codeBytes", codeBytes);
        global.Count("decode.decodedBytes", decoded.Count(value => value));
        global.Count("decode.liveBytes", liveBytes.Count(value => value));
        global.Count("decode.instructions", a.Flow.Instructions.Count);
        global.Count("decode.liveInstructions", liveInstructions.Count);
        return new JsonObject
        {
            ["codeBytes"] = codeBytes,
            ["decodedBytes"] = decoded.Count(value => value),
            ["liveBytes"] = liveBytes.Count(value => value),
            ["instructions"] = a.Flow.Instructions.Count,
            ["liveInstructions"] = liveInstructions.Count,
            ["anomalies"] = anomalies
        };
    }

    private JsonArray EntitiesJson()
    {
        var models = a.File.ModelResourceNames();
        var array = new JsonArray();
        foreach (var entity in a.Entities)
        {
            var scriptEntity = a.Section.Entities[entity.Index];
            global.Count($"entity.kind.{entity.Kind}");
            if (entity.MainOffsets.Count > 1)
            {
                global.Count("entity.initMainAmbiguous");
                AddCandidate("InitMainSplitAmbiguous", 3,
                    $"{entity.Name} (entity {entity.Index}): main can start at {string.Join(", ", entity.MainOffsets)}; Makou {entity.MakouMainOffset?.ToString() ?? "none"}",
                    new JsonObject
                    {
                        ["entity"] = entity.Index,
                        ["initReturns"] = Array(entity.InitReturns),
                        ["mains"] = Array(entity.MainOffsets)
                    });
            }

            if (entity.MakouMainOffset is null)
            {
                global.Count("entity.noMakouSplit");
            }

            var slots = new JsonArray();
            for (var slot = 0; slot < a.Section.SlotCount; slot++)
            {
                var slotInfo = scriptEntity.Slots[slot];
                var entry = a.EntriesById[$"e{entity.Index}.s{slot}"];
                var shared = a.Section.EntriesAt(slotInfo.Pointer)
                    .Where(other => other.Entity != entity.Index)
                    .Select(other => $"e{other.Entity}.s{other.Slot}")
                    .ToArray();
                if (slotInfo.AliasOf is not null)
                {
                    global.Count("slot.aliased");
                }

                if (shared.Length > 0)
                {
                    global.Count("slot.sharedAcrossEntities");
                }

                if (!a.Section.IsInCode(slotInfo.Pointer))
                {
                    global.Count("slot.outsideCode");
                }

                slots.Add(new JsonObject
                {
                    ["slot"] = slot,
                    ["pointer"] = slotInfo.Pointer,
                    ["kind"] = entry.Kind.ToString(),
                    ["aliasOf"] = slotInfo.AliasOf,
                    ["sharedWith"] = shared.Length == 0 ? null : Array(shared),
                    ["shippingRange"] = slotInfo.ShippingRange is { } s ? new JsonArray(s.Start, s.End) : null,
                    ["makouRange"] = slotInfo.MakouRange is { } m ? new JsonArray(m.Start, m.End) : null,
                    ["engineTriggered"] = entry.EngineTriggered,
                    ["live"] = entry.Live,
                    ["reach"] = entry.Reach.Count,
                    ["calledFrom"] = entry.CalledFrom.Count == 0 ? null : Array(entry.CalledFrom)
                });
            }

            array.Add(new JsonObject
            {
                ["index"] = entity.Index,
                ["name"] = entity.Name,
                ["kind"] = entity.Kind.ToString(),
                ["models"] = Array(entity.ModelIds),
                ["resources"] = Array(entity.ModelIds.Select(id => id < models.Count ? models[id] : "<invalid>")),
                ["partyCharacters"] = Array(entity.PartyCharacters),
                ["line"] = entity.Line is null ? null : Array(entity.Line),
                ["init"] = new JsonObject
                {
                    ["offset"] = entity.InitOffset,
                    ["returns"] = Array(entity.InitReturns),
                    ["makouMain"] = entity.MakouMainOffset,
                    ["mains"] = Array(entity.MainOffsets)
                },
                ["state"] = EntityStateJson(entity),
                ["issues"] = Array(entity.Issues),
                ["makouEmptyGroup"] = scriptEntity.MakouEmptyGroup,
                ["slots"] = slots
            });
        }

        return array;
    }

    /// <summary>
    /// The entity's own state opcodes (they act on the entity running them), with where
    /// they run and what guards them.
    /// </summary>
    private JsonArray EntityStateJson(EntityModel entity)
    {
        var array = new JsonArray();
        foreach (var entry in a.Entries.Where(entry => entry.Entity == entity.Index && entry.Live))
        {
            foreach (var at in entry.Reach)
            {
                if (!a.EffectsAt.TryGetValue(at, out var effects))
                {
                    continue;
                }

                foreach (var effect in effects.Where(effect => effect.Kind is "TalkEnable" or "Visibility" or "Solid" or "LineEnable" or "TalkRange" or "SolidRange" or "Place" or "ModelLoad" or "CharacterBind" or "LineDefine" or "LineMove"))
                {
                    array.Add(new JsonObject
                    {
                        ["entry"] = entry.Id,
                        ["at"] = at,
                        ["kind"] = effect.Kind,
                        ["detail"] = effect.Detail,
                        ["must"] = Guards(entry.Must[at].Select(Literal.FromKey))
                    });
                }
            }
        }

        return array;
    }

    private JsonArray EntriesJson()
    {
        var array = new JsonArray();
        foreach (var entry in a.Entries)
        {
            if (entry.Slot >= 0 && entry.AliasOf is not null && !entry.Live)
            {
                // A dormant alias adds nothing its first slot does not already list.
                continue;
            }

            global.Count("entry.total");
            if (entry.Live)
            {
                global.Count("entry.live");
            }

            var calls = new JsonArray();
            foreach (var site in a.SitesIn(entry.Id))
            {
                global.Count($"call.{site.Call.Kind}");
                if (site.Problem is not null)
                {
                    global.Count($"call.problem.{ProblemKey(site.Problem)}");
                }

                calls.Add(new JsonObject
                {
                    ["at"] = site.At,
                    ["op"] = Opcodes.Name(a.Flow.Instructions[site.At].Op),
                    ["target"] = site.Call.Target,
                    ["script"] = site.Call.Script,
                    ["priority"] = site.Call.Priority,
                    ["targets"] = Array(site.Targets),
                    ["problem"] = site.Problem,
                    ["must"] = Guards(entry.Must[site.At].Select(Literal.FromKey))
                });
            }

            var effects = new JsonArray();
            foreach (var at in entry.Reach)
            {
                if (!a.EffectsAt.TryGetValue(at, out var list))
                {
                    continue;
                }

                foreach (var effect in list)
                {
                    effects.Add(new JsonObject
                    {
                        ["at"] = at,
                        ["kind"] = effect.Kind,
                        ["detail"] = effect.Detail,
                        ["must"] = Guards(entry.Must[at].Select(Literal.FromKey)),
                        ["pathDependent"] = entry.May[at].Count - entry.Must[at].Count
                    });
                }
            }

            array.Add(new JsonObject
            {
                ["id"] = entry.Id,
                ["entity"] = entry.Entity,
                ["slot"] = entry.Slot,
                ["kind"] = entry.Kind.ToString(),
                ["offset"] = entry.Offset,
                ["engineTriggered"] = entry.EngineTriggered,
                ["live"] = entry.Live,
                ["aliasOf"] = entry.AliasOf,
                ["reach"] = entry.Reach.Count,
                ["calledFrom"] = Array(entry.CalledFrom),
                ["calls"] = calls,
                ["effects"] = effects
            });
        }

        return array;
    }

    private static string ProblemKey(string problem) =>
        problem.StartsWith("target slot aliases", StringComparison.Ordinal) ? "aliasedTarget"
        : problem.StartsWith("dynamic", StringComparison.Ordinal) ? "dynamicParty"
        : problem.StartsWith("entity", StringComparison.Ordinal) ? "missingEntity"
        : problem.StartsWith("target pointer", StringComparison.Ordinal) ? "targetOutsideCode"
        : problem.StartsWith("party slot request with no", StringComparison.Ordinal) ? "partyWithoutCharacter"
        : "other";

    public string Trigger(EntryModel root, ClosureEffect effect)
    {
        var confirm = effect.Must.Any(literal => literal.Holds &&
                                                 a.Conditions.TryGetValue(literal.At, out var condition) &&
                                                 condition.IsConfirmKey);
        var position = effect.Must.Any(literal => a.Conditions.TryGetValue(literal.At, out var condition) &&
                                                  ReadsPosition(condition));
        return root.Kind switch
        {
            EntryKind.Talk => "talk",
            EntryKind.Contact => confirm ? "touch+confirm" : "touch",
            EntryKind.LineOk => "line-confirm",
            EntryKind.LineMove or EntryKind.LineMoveAlt => confirm ? "line-move+confirm" : "line-move",
            EntryKind.LineGo or EntryKind.LineGoOnce => confirm ? "line-enter+confirm" : "line-enter",
            EntryKind.LineGoAway => "line-leave",
            EntryKind.Init => confirm ? "arrival+confirm" : position ? "arrival+position" : "arrival",
            EntryKind.Main => confirm ? "main-confirm" : position ? "main-position" : "main-automatic",
            _ => "script"
        };
    }

    public static bool IsPlayerTrigger(string trigger) =>
        trigger is not ("arrival" or "main-automatic" or "script");

    private bool ReadsPosition(Condition condition) =>
        new[] { condition.Left?.Variable, condition.Right?.Variable }
            .Where(variable => variable is not null)
            .SelectMany(variable => ByteKey.Of(variable!))
            .Any(key => a.PositionVariables.Contains(key.ToString()));

    private JsonArray InteractionsJson(IReadOnlyList<EntryModel> roots)
    {
        var array = new JsonArray();
        foreach (var root in roots)
        {
            var (effects, depthLimited) = closures[root.Id];
            var interesting = effects
                .Where(effect => effect.Effect.Kind is not ("Sound" or "Animation" or "TempWrite"))
                .ToArray();
            if (interesting.Length == 0 && root.Kind is EntryKind.Init or EntryKind.Main)
            {
                continue;
            }

            if (depthLimited)
            {
                global.Count("closure.depthLimited");
            }

            var list = new JsonArray();
            foreach (var effect in effects)
            {
                list.Add(new JsonObject
                {
                    ["at"] = effect.Effect.At,
                    ["kind"] = effect.Effect.Kind,
                    ["detail"] = effect.Effect.Detail,
                    ["trigger"] = Trigger(root, effect),
                    ["in"] = effect.EntryId,
                    ["via"] = effect.Chain.Count > 1 ? Array(effect.Chain) : null,
                    ["dynamic"] = effect.Dynamic ? true : null,
                    ["must"] = Guards(effect.Must),
                    ["pathDependent"] = effect.PathDependent == 0 ? null : effect.PathDependent
                });
            }

            array.Add(new JsonObject
            {
                ["root"] = root.Id,
                ["entity"] = root.Entity,
                ["entityName"] = a.Entities[root.Entity].Name,
                ["entityKind"] = a.Entities[root.Entity].Kind.ToString(),
                ["kind"] = root.Kind.ToString(),
                ["depthLimited"] = depthLimited,
                ["effects"] = list
            });
        }

        return array;
    }

    private JsonObject TriggersJson()
    {
        var triggers = a.File.ReadTriggers();
        var result = new JsonObject { ["walkmeshTriangles"] = a.File.WalkmeshTriangleCount() };
        if (triggers is null)
        {
            return result;
        }

        var (gateways, backgroundTriggers, arrows, control) = triggers.Value;
        global.Count("gateway.total", gateways.Count);
        global.Count("arrow.total", arrows.Count);
        global.Count("arrow.visible", arrows.Count(arrow => arrow.Type != 0));
        result["control"] = control;
        result["gateways"] = new JsonArray(gateways.Select(gateway => (JsonNode)new JsonObject
        {
            ["index"] = gateway.Index,
            ["destination"] = gateway.Destination,
            ["line"] = new JsonArray(gateway.Line.Select(vertex => (JsonNode)new JsonArray(vertex.X, vertex.Y, vertex.Z)).ToArray()),
            ["arrival"] = new JsonArray(gateway.ArrivalX, gateway.ArrivalY, gateway.ArrivalTriangle),
            ["direction"] = gateway.Direction
        }).ToArray());
        result["backgroundTriggers"] = new JsonArray(backgroundTriggers.Select(trigger => (JsonNode)new JsonObject
        {
            ["index"] = trigger.Index,
            ["line"] = new JsonArray(trigger.Line.Select(vertex => (JsonNode)new JsonArray(vertex.X, vertex.Y, vertex.Z)).ToArray()),
            ["parameter"] = trigger.Parameter,
            ["state"] = trigger.State,
            ["behavior"] = trigger.Behavior,
            ["sound"] = trigger.Sound
        }).ToArray());
        result["arrows"] = new JsonArray(arrows.Select(arrow => (JsonNode)new JsonObject
        {
            ["index"] = arrow.Index,
            ["position"] = new JsonArray(arrow.X, arrow.Y, arrow.Z),
            ["type"] = arrow.Type switch { 0 => "invisible", 1 => "red", 2 => "green", _ => $"type{arrow.Type}" },
            ["displayFlag"] = arrow.DisplayFlag
        }).ToArray());
        return result;
    }

    private JsonObject ShippingJson(IReadOnlyList<EntryModel> roots)
    {
        var result = new JsonObject
        {
            ["readUsable"] = shipping?.IsUsable,
            ["diagnostic"] = shipping?.Diagnostic
        };
        if (view is null)
        {
            return result;
        }

        result["parity"] = ParityJson();
        // The released walker is explained rule by rule; the repaired one is compared on
        // what it publishes, because it keeps only the actions that decide a landing.
        result["walkerModel"] = ShippingReflection.IsRepairedCatalog ? "published-catalog" : "baseline-0.6.8";
        result["walker"] = ShippingReflection.IsRepairedCatalog ? CatalogWalkerJson() : WalkerJson();
        result["npcs"] = NpcJson(roots);
        result["pickups"] = PickupJson(roots);
        result["exits"] = ExitJson(roots);
        result["calls"] = CallFindingsJson();
        return result;
    }

    private JsonObject ParityJson()
    {
        var scripts = new JsonArray();
        var escapesTotal = 0;
        var desyncTotal = 0;
        var truncatedTotal = 0;
        var deadTotal = 0;
        foreach (var mismatch in view!.Mismatches)
        {
            global.Count("parity.replicaMismatch");
        }

        foreach (var ((entity, slot), start) in view.Starts.OrderBy(pair => pair.Key))
        {
            var slice = view.Groups[entity].Scripts[slot];
            var end = start + slice.Length;
            var sweep = view.Opcodes(entity, slot);
            var swept = sweep.Select(opcode => start + opcode.Offset).ToHashSet();
            var sweptBytes = sweep.Sum(opcode => opcode.Bytes.Length);
            int? stoppedAt = sweptBytes < slice.Length ? start + sweptBytes : null;
            var entry = a.EntriesById[$"e{entity}.s{slot}"];
            var escapes = entry.Reach.Where(at => at < start || at >= end).ToArray();
            var engineInside = a.Flow.Instructions.Keys.Where(at => at >= start && at < end).ToHashSet();
            var engineOnly = engineInside.Where(at => !swept.Contains(at)).ToArray();
            var dead = swept.Where(at => !liveInstructions.Contains(at)).ToArray();
            var desync = swept.Where(at => !a.Flow.Instructions.ContainsKey(at) && IsInsideDecoded(at)).ToArray();
            global.Count("parity.shippingScripts");
            global.Count("parity.sweptInstructions", swept.Count);
            if (stoppedAt is not null)
            {
                truncatedTotal++;
                global.Count("parity.sweepStoppedEarly");
            }

            if (escapes.Length > 0)
            {
                escapesTotal++;
                global.Count("parity.scriptsWithEscapes");
                global.Count("parity.escapedInstructions", escapes.Length);
            }

            if (engineOnly.Length > 0)
            {
                global.Count("parity.scriptsWithEngineOnlyInstructions");
                global.Count("parity.engineOnlyInstructions", engineOnly.Length);
            }

            if (desync.Length > 0)
            {
                desyncTotal++;
                global.Count("parity.scriptsWithDesync");
            }

            deadTotal += dead.Length;
            global.Count("parity.deadSweptInstructions", dead.Length);
            if (escapes.Length > 0 || engineOnly.Length > 0 || desync.Length > 0 || stoppedAt is not null)
            {
                scripts.Add(new JsonObject
                {
                    ["entity"] = entity,
                    ["slot"] = slot,
                    ["range"] = new JsonArray(start, end),
                    ["swept"] = swept.Count,
                    ["stoppedAt"] = stoppedAt,
                    ["escapes"] = Array(escapes.Take(12)),
                    ["escapeCount"] = escapes.Length,
                    ["engineOnly"] = Array(engineOnly.Take(12)),
                    ["engineOnlyCount"] = engineOnly.Length,
                    ["desync"] = Array(desync.Take(12)),
                    ["dead"] = dead.Length
                });
            }
        }

        return new JsonObject
        {
            ["replicaMismatches"] = Array(view.Mismatches),
            ["scriptsWithEscapes"] = escapesTotal,
            ["scriptsWithDesync"] = desyncTotal,
            ["scriptsStoppedEarly"] = truncatedTotal,
            ["deadSweptInstructions"] = deadTotal,
            ["scripts"] = scripts
        };
    }

    private bool IsInsideDecoded(int at)
    {
        for (var start = at - 1; start >= Math.Max(a.Section.CodeStart, at - 160); start--)
        {
            if (a.Flow.Instructions.TryGetValue(start, out var instruction))
            {
                return instruction.End > at;
            }
        }

        return false;
    }

    /// <summary>
    /// For every line entity the shipping catalog walks, what the engine can do from the
    /// line's walk events (Move, Go, Go 1x, Go away: slots 2-6) and its [OK] handler (slot
    /// 1), against what the shipping walker itself collects from the same entity, compared
    /// by what each navigation does (destination field, or landing coordinates and
    /// triangle). Every difference is then explained by replaying the walker's control flow
    /// with one shipping rule at a time replaced by the engine's.
    /// </summary>
    private JsonArray WalkerJson()
    {
        var array = new JsonArray();
        var walks = new Dictionary<WalkFix, BaselineShippingWalk>();
        BaselineShippingWalk Walk(WalkFix fix)
        {
            if (!walks.TryGetValue(fix, out var walk))
            {
                walk = new BaselineShippingWalk(view!, fix);
                walks[fix] = walk;
            }

            return walk;
        }

        foreach (var entity in a.Entities.Where(entity => view!.IsLineGroup(entity.Index)))
        {
            var realWalk = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var realOk = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var shippingSlots = view!.Groups[entity.Index].Scripts.Keys.Order().ToArray();
            foreach (var slot in shippingSlots)
            {
                var isEntry = slot > 1 || IsVerifiedActionActivatedExitScript(a.FieldId, entity.Index, slot);
                if (!isEntry && slot != 1)
                {
                    continue;
                }

                IReadOnlyList<ShippingAction> actions;
                try
                {
                    actions = ShippingReflection.CollectNavigationActions(view.Native, entity.Index, slot);
                }
                catch (Exception exception)
                {
                    a.Issues.Add($"shipping walker failed for entity {entity.Index} slot {slot}: {exception.GetBaseException().Message}");
                    continue;
                }

                foreach (var action in actions)
                {
                    var sink = isEntry ? realWalk : realOk;
                    if (!sink.TryGetValue(action.Signature, out var sources))
                    {
                        sources = [];
                        sink[action.Signature] = sources;
                    }

                    sources.Add($"s{slot}:e{action.SourceGroup}.s{action.SourceScript}");
                }
            }

            var engineWalk = EngineNavigationSignatures(entity.Index, [2, 3, 4, 5, 6]);
            var engineOk = EngineNavigationSignatures(entity.Index, [1]);
            var engineAny = engineWalk.Keys.Concat(engineOk.Keys).ToHashSet(StringComparer.Ordinal);
            var realAny = realWalk.Keys.Concat(realOk.Keys).ToHashSet(StringComparer.Ordinal);
            var selfJump = $"MapJump {a.FieldId}";
            var missingExits = engineWalk.Keys
                .Where(signature => signature.StartsWith("MapJump ", StringComparison.Ordinal) &&
                                    signature != selfJump &&
                                    !realWalk.ContainsKey(signature))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var missingMoves = engineAny
                .Where(signature => !signature.StartsWith("MapJump ", StringComparison.Ordinal) &&
                                    !signature.StartsWith("dynamic ", StringComparison.Ordinal) &&
                                    !realAny.Contains(signature))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var partySignatures = EngineNavigationSignatures(entity.Index, [1, 2, 3, 4, 5, 6], dynamic: true).Keys
                .ToHashSet(StringComparer.Ordinal);
            var phantoms = realAny.Where(signature => !engineAny.Contains(signature) && !partySignatures.Contains(signature))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var okOnlyExits = engineOk.Keys
                .Where(signature => signature.StartsWith("MapJump ", StringComparison.Ordinal) &&
                                    signature != selfJump &&
                                    !engineWalk.ContainsKey(signature) &&
                                    !realWalk.ContainsKey(signature))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var dynamicNavigation = engineAny.Where(signature => signature.StartsWith("dynamic ", StringComparison.Ordinal)).ToArray();

            // A party-slot request runs whoever is leading, and the engine view keeps every
            // candidate's copy. The shipping walker keeps one copy only when the copies agree,
            // so a party routine is lost only when none of its copies was collected.
            var partyWalk = EngineNavigationSignatures(entity.Index, [1, 2, 3, 4, 5, 6], dynamic: true);
            var partyStatic = partyWalk.Keys.Where(signature => !signature.StartsWith("dynamic ", StringComparison.Ordinal)).ToArray();
            var partyCollected = partyStatic.Where(realAny.Contains).ToArray();
            if (partyStatic.Length > 0)
            {
                global.Count("walker.partyRoutines");
                if (partyCollected.Length == 0)
                {
                    global.Count("walker.partyRoutinesNotCollected");
                    AddCandidate("PartyRoutineNotCollected", 2,
                        $"line {entity.Name} (entity {entity.Index}): its events ask the party to run {partyStatic.Length} navigation variants ({string.Join("; ", partyStatic.Take(3))}); the shipping walker collects none",
                        new JsonObject
                        {
                            ["entity"] = entity.Index,
                            ["variants"] = Array(partyStatic),
                            ["variantSources"] = Array(partyWalk.Where(pair => partyStatic.Contains(pair.Key))
                                .SelectMany(pair => pair.Value.Select(at => $"{pair.Key} @{at} in {string.Join(",", a.Entries.Where(entry => entry.Reach.Contains(at)).Select(entry => entry.Id).Take(4))}"))),
                            ["eventSlotAliases"] = Array(EventSlotAliases(entity.Index)),
                            ["repro"] = Repro
                        });
                }
            }
            global.Count("walker.lineEntities");
            global.Count("walker.missingExits", missingExits.Length);
            global.Count("walker.missingMoves", missingMoves.Length);
            global.Count("walker.phantoms", phantoms.Length);
            global.Count("walker.okOnlyExits", okOnlyExits.Length);
            global.Count("walker.dynamicNavigation", dynamicNavigation.Length);

            JsonObject Explain(string signature, bool fromWalk)
            {
                var offsets = (fromWalk
                        ? engineWalk.GetValueOrDefault(signature)
                        : engineOk.GetValueOrDefault(signature) ?? engineWalk.GetValueOrDefault(signature))
                    ?? [];
                var slots = fromWalk ? new[] { 2, 3, 4, 5, 6 } : new[] { 1, 2, 3, 4, 5, 6 };
                // Only the event slots whose own closure runs the instruction matter.
                slots = slots.Where(slot => closures.TryGetValue($"e{entity.Index}.s{slot}", out var closure) &&
                                            closure.Effects.Any(effect => offsets.Contains(effect.Effect.At)))
                    .ToArray();
                string? mechanism = null;
                JsonObject? folding = null;
                bool VisitedWith(WalkFix fix) =>
                    slots.Any(slot => offsets.Any(at => Walk(fix).Visit(entity.Index, slot).Contains(at)));
                var scriptSlots = a.Section.Entities[entity.Index].Slots;
                if (fromWalk && slots.Length > 0 && slots.All(slot => scriptSlots[slot].AliasOf is 1 or 0))
                {
                    mechanism = "walk event slot shares the [OK] handler's pointer; the walker treats that code as [OK] only";
                }
                else if (VisitedWith(WalkFix.None))
                {
                    folding = ConstantFolding(entity.Index, slots, offsets);
                    var viaParty = slots.Any(slot => closures[$"e{entity.Index}.s{slot}"].Effects
                        .Any(effect => offsets.Contains(effect.Effect.At) && effect.Dynamic));
                    mechanism = folding?["kind"]?.GetValue<string>() switch
                    {
                        "another script writes it" => "constant folding across a variable another script writes",
                        not null => "constant folding keeps a value a later write changed",
                        null when viaParty => "party routine without agreement",
                        null => "walker reaches the instruction; constant folding or path limit drops it"
                    };
                }
                else
                {
                    foreach (var fix in new[] { WalkFix.Jmpfl, WalkFix.AllConditionals, WalkFix.Retto, WalkFix.AliasedRequests, WalkFix.CrossSlice })
                    {
                        if (VisitedWith(fix))
                        {
                            mechanism = fix switch
                            {
                                WalkFix.Jmpfl => "JMPFL measured from byte 2 instead of the operand",
                                WalkFix.AllConditionals => "IFKEY/IFPRTYQ/IFMEMBQ not forked",
                                WalkFix.Retto => "RETTO not followed",
                                WalkFix.AliasedRequests => "request of an aliased slot dropped",
                                _ => "control flow leaves the script's slice"
                            };
                            break;
                        }
                    }

                    if (mechanism is null && VisitedWith(WalkFix.All))
                    {
                        mechanism = "several shipping rules together";
                    }
                }

                if (mechanism is null)
                {
                    var missingSlots = slots.Where(slot => !view.HasScript(entity.Index, slot)).ToArray();
                    mechanism = missingSlots.Length > 0
                        ? $"event slots {string.Join(",", missingSlots)} share another slot's pointer and are not walked"
                        : "unexplained";
                }

                global.Count($"walker.mechanism.{mechanism}");
                return new JsonObject
                {
                    ["signature"] = signature,
                    ["engineAt"] = Array(offsets),
                    ["mechanism"] = mechanism,
                    ["folding"] = folding,
                    ["eventSlotAliases"] = Array(EventSlotAliases(entity.Index))
                };
            }

            var missingExitJson = new JsonArray(missingExits.Select(signature => (JsonNode)Explain(signature, true)).ToArray());
            var missingMoveJson = new JsonArray(missingMoves.Select(signature => (JsonNode)Explain(signature, false)).ToArray());
            foreach (var node in missingExitJson)
            {
                var item = (JsonObject)node!;
                AddCandidate("LineExitMissedByWalker", 1,
                    $"line {entity.Name} (entity {entity.Index}): its walk events can run {item["signature"]}; the shipping walker does not collect it ({item["mechanism"]})",
                    WalkerWitness(entity.Index, item, [2, 3, 4, 5, 6]));
            }

            foreach (var node in missingMoveJson)
            {
                var item = (JsonObject)node!;
                AddCandidate("LineTraversalMissedByWalker", 2,
                    $"line {entity.Name} (entity {entity.Index}): its events can run {item["signature"]}; the shipping walker does not collect it ({item["mechanism"]})",
                    WalkerWitness(entity.Index, item, [1, 2, 3, 4, 5, 6]));
            }

            foreach (var signature in phantoms)
            {
                var sources = realWalk.GetValueOrDefault(signature) ?? realOk.GetValueOrDefault(signature) ?? [];
                AddCandidate("WalkerPhantomNavigation", signature.StartsWith("MapJump ", StringComparison.Ordinal) ? 1 : 2,
                    $"line {entity.Name} (entity {entity.Index}): the shipping walker collects {signature} from {string.Join(",", sources.Distinct().Take(4))}, which no event of this line can run",
                    new JsonObject
                    {
                        ["entity"] = entity.Index,
                        ["signature"] = signature,
                        ["shippingSources"] = Array(sources.Distinct()),
                        ["engineInstructions"] = Array(EngineInstructionsWithSignature(signature)),
                        ["engineEntriesReaching"] = Array(EngineInstructionsWithSignature(signature)
                            .SelectMany(at => a.Entries.Where(entry => entry.Reach.Contains(at))
                                .Select(entry => $"{entry.Id}{(entry.Live ? string.Empty : " (dormant)")}"))
                            .Distinct()
                            .Take(12)),
                        ["eventSlotAliases"] = Array(EventSlotAliases(entity.Index)),
                        ["repro"] = Repro
                    });
            }

            foreach (var signature in okOnlyExits)
            {
                global.Count("walker.confirmOnlyExit");
                AddCandidate("ConfirmLineExitNotOffered", 2,
                    $"line {entity.Name} (entity {entity.Index}): only its [OK] handler can run {signature}; the catalog leaves [OK] map jumps out of Exits by design",
                    WalkerWitness(entity.Index, new JsonObject { ["signature"] = signature, ["engineAt"] = Array(engineOk[signature]) }, [1]));
            }

            array.Add(new JsonObject
            {
                ["entity"] = entity.Index,
                ["name"] = entity.Name,
                ["shippingSlots"] = Array(shippingSlots),
                ["eventSlotAliases"] = Array(EventSlotAliases(entity.Index)),
                ["engineWalk"] = Array(engineWalk.Keys.Order(StringComparer.Ordinal)),
                ["engineOk"] = Array(engineOk.Keys.Order(StringComparer.Ordinal)),
                ["shippingWalk"] = Array(realWalk.Keys.Order(StringComparer.Ordinal)),
                ["shippingOk"] = Array(realOk.Keys.Order(StringComparer.Ordinal)),
                ["missingExits"] = missingExitJson,
                ["missingMoves"] = missingMoveJson,
                ["phantoms"] = Array(phantoms),
                ["confirmOnlyExits"] = Array(okOnlyExits),
                ["dynamicNavigation"] = Array(dynamicNavigation),
                ["partyNavigation"] = Array(partyStatic),
                ["partyCollected"] = Array(partyCollected)
            });
        }

        return array;
    }

    /// <summary>
    /// For every line entity, what the engine can do from the line's walk events (Move, Go,
    /// Go 1x, Go away: slots 2-6) and its [OK] handler (slot 1), against what the production
    /// catalog publishes for that line: its script exits' destinations and its transitions'
    /// landings. This is the comparison for the repaired catalog, whose walker keeps only the
    /// actions that decide a landing, so it is compared on what it publishes rather than on
    /// every movement it passes through.
    ///
    /// <para>The engine side is the audit's own model: the closure of each event script, with
    /// its own reading of which models can ever be the controlled one (a party character
    /// bound in Init, a CC target, or the first model an Init loads) and its own control flow
    /// for whether a movement can be the last one before its script ends. A landing the
    /// engine can reach that the catalog does not publish is reported unless the model making
    /// it is never controlled or every way on from it moves the model again; a landing or exit
    /// the catalog publishes that no event of the line can reach is a phantom.</para>
    /// </summary>
    private JsonArray CatalogWalkerJson()
    {
        var array = new JsonArray();
        if (shipping is not { IsUsable: true } published)
        {
            return array;
        }

        var controllable = ControllableEntities();
        foreach (var entity in a.Entities.Where(entity => entity.Kind == EntityKind.Line))
        {
            var engineWalk = EngineNavigationSignatures(entity.Index, [2, 3, 4, 5, 6]);
            var engineOk = EngineNavigationSignatures(entity.Index, [1]);
            var engineParty = EngineNavigationSignatures(entity.Index, [1, 2, 3, 4, 5, 6], dynamic: true);
            var engineAny = engineWalk.Keys.Concat(engineOk.Keys).Concat(engineParty.Keys).ToHashSet(StringComparer.Ordinal);
            var selfJump = $"MapJump {a.FieldId}";

            var exits = published.Exits
                .Where(exit => exit.TriggerEntityId == entity.Index)
                .SelectMany(exit => exit.DestinationFieldIds ?? [])
                .Select(destination => $"MapJump {destination}")
                .ToHashSet(StringComparer.Ordinal);
            // The line's own traversals: not the ones the field polls for, and not the reverse
            // ladders the catalog derives from another routine's LADER.
            var landings = published.Transitions
                .Where(transition => transition.SourceEntityId == entity.Index &&
                                     transition.SourceTriangle < 0 &&
                                     !transition.StableId.StartsWith("ladder-auto:", StringComparison.Ordinal))
                .GroupBy(transition => LandingKey(transition.TargetX, transition.TargetY, transition.TargetTriangle))
                .ToDictionary(group => group.Key, group => group.Select(transition => transition.StableId).ToArray(), StringComparer.Ordinal);

            var engineLandings = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var (signature, offsets) in engineWalk.Concat(engineOk).Concat(engineParty))
            {
                if (signature.StartsWith("MapJump ", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var at in offsets)
                {
                    if (EngineLandingKey(at) is not { } key)
                    {
                        continue;
                    }

                    if (!engineLandings.TryGetValue(key, out var list))
                    {
                        list = [];
                        engineLandings[key] = list;
                    }

                    if (!list.Contains(at))
                    {
                        list.Add(at);
                    }
                }
            }

            var unpublishedExits = engineWalk.Keys
                .Where(signature => signature.StartsWith("MapJump ", StringComparison.Ordinal) &&
                                    signature != selfJump && !exits.Contains(signature))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var explainedExits = new JsonArray();
            var missingExits = new List<string>();
            foreach (var signature in unpublishedExits)
            {
                if (ExplainExclusion(entity.Index, engineWalk[signature], [2, 3, 4, 5, 6]) is { } reason)
                {
                    explainedExits.Add(new JsonObject { ["signature"] = signature, ["reason"] = reason });
                    global.Count($"catalog.explainedExit.{reason}");
                }
                else
                {
                    missingExits.Add(signature);
                }
            }

            var phantomExits = exits.Where(signature => !engineAny.Contains(signature)).Order(StringComparer.Ordinal).ToArray();
            var phantomLandings = landings.Keys.Where(key => !engineLandings.ContainsKey(key)).Order(StringComparer.Ordinal).ToArray();
            var okOnlyExits = engineOk.Keys
                .Where(signature => signature.StartsWith("MapJump ", StringComparison.Ordinal) &&
                                    signature != selfJump && !engineWalk.ContainsKey(signature) && !exits.Contains(signature))
                .Order(StringComparer.Ordinal)
                .ToArray();

            var notControlled = new List<string>();
            var intermediate = new List<string>();
            var explainedLandings = new JsonArray();
            var missingLandings = new List<JsonObject>();
            foreach (var (key, offsets) in engineLandings.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (landings.ContainsKey(key))
                {
                    continue;
                }

                var movers = offsets.Select(MoverOf).Where(mover => mover >= 0).Distinct().ToArray();
                if (movers.Length > 0 && movers.All(mover => !controllable.Contains(mover)))
                {
                    notControlled.Add(key);
                    continue;
                }

                if (offsets.All(IsIntermediateMovement))
                {
                    intermediate.Add(key);
                    continue;
                }

                if (ExplainExclusion(entity.Index, offsets, [1, 2, 3, 4, 5, 6]) is { } reason)
                {
                    explainedLandings.Add(new JsonObject { ["landing"] = key, ["reason"] = reason, ["engineAt"] = Array(offsets) });
                    global.Count($"catalog.explainedLanding.{reason}");
                    continue;
                }

                missingLandings.Add(new JsonObject
                {
                    ["landing"] = key,
                    ["engineAt"] = Array(offsets),
                    ["movers"] = Array(movers),
                    ["eventSlotAliases"] = Array(EventSlotAliases(entity.Index))
                });
            }

            global.Count("catalog.lineEntities");
            global.Count("catalog.missingExits", missingExits.Count);
            global.Count("catalog.explainedExits", explainedExits.Count);
            global.Count("catalog.explainedLandings", explainedLandings.Count);
            global.Count("catalog.phantomExits", phantomExits.Length);
            global.Count("catalog.phantomLandings", phantomLandings.Length);
            global.Count("catalog.missingLandings", missingLandings.Count);
            global.Count("catalog.landingsOfUncontrolledModels", notControlled.Count);
            global.Count("catalog.intermediateLandings", intermediate.Count);
            global.Count("catalog.confirmOnlyExits", okOnlyExits.Length);

            foreach (var signature in missingExits)
            {
                AddCandidate("LineExitMissedByCatalog", 1,
                    $"line {entity.Name} (entity {entity.Index}): its walk events can run {signature}; the catalog does not publish it",
                    WalkerWitness(entity.Index, new JsonObject { ["signature"] = signature, ["engineAt"] = Array(engineWalk[signature]) }, [2, 3, 4, 5, 6]));
            }

            foreach (var signature in phantomExits)
            {
                AddCandidate("CatalogPhantomExit", 1,
                    $"line {entity.Name} (entity {entity.Index}): the catalog publishes {signature}, which no event of this line can run",
                    new JsonObject
                    {
                        ["entity"] = entity.Index,
                        ["signature"] = signature,
                        ["engineInstructions"] = Array(EngineInstructionsWithSignature(signature)),
                        ["repro"] = Repro
                    });
            }

            foreach (var key in phantomLandings)
            {
                AddCandidate("CatalogPhantomLanding", 2,
                    $"line {entity.Name} (entity {entity.Index}): the catalog publishes a landing at {key} ({string.Join(", ", landings[key].Take(3))}), which no movement an event of this line can run ends at",
                    new JsonObject
                    {
                        ["entity"] = entity.Index,
                        ["landing"] = key,
                        ["transitions"] = Array(landings[key]),
                        ["repro"] = Repro
                    });
            }

            foreach (var item in missingLandings)
            {
                AddCandidate("LineLandingMissedByCatalog", 2,
                    $"line {entity.Name} (entity {entity.Index}): its events can end a controllable model's movement at {item["landing"]}; the catalog does not publish that landing",
                    WalkerWitness(entity.Index, item, [1, 2, 3, 4, 5, 6]));
            }

            foreach (var signature in okOnlyExits)
            {
                AddCandidate("ConfirmLineExitNotOffered", 2,
                    $"line {entity.Name} (entity {entity.Index}): only its [OK] handler can run {signature}; the catalog leaves [OK] map jumps out of Exits by design",
                    WalkerWitness(entity.Index, new JsonObject { ["signature"] = signature, ["engineAt"] = Array(engineOk[signature]) }, [1]));
            }

            array.Add(new JsonObject
            {
                ["entity"] = entity.Index,
                ["name"] = entity.Name,
                ["engineWalk"] = Array(engineWalk.Keys.Order(StringComparer.Ordinal)),
                ["engineOk"] = Array(engineOk.Keys.Order(StringComparer.Ordinal)),
                ["engineParty"] = Array(engineParty.Keys.Order(StringComparer.Ordinal)),
                ["publishedExits"] = Array(exits.Order(StringComparer.Ordinal)),
                ["publishedLandings"] = Array(landings.Keys.Order(StringComparer.Ordinal)),
                ["missingExits"] = Array(missingExits),
                ["explainedExits"] = explainedExits,
                ["explainedLandings"] = explainedLandings,
                ["phantomExits"] = Array(phantomExits),
                ["phantomLandings"] = Array(phantomLandings),
                ["missingLandings"] = new JsonArray(missingLandings.Select(item => (JsonNode)item.DeepClone()).ToArray()),
                ["landingsOfUncontrolledModels"] = Array(notControlled),
                ["intermediateLandings"] = Array(intermediate),
                ["confirmOnlyExits"] = Array(okOnlyExits)
            });
        }

        return array;
    }

    private static string LandingKey(int x, int y, int triangle) => $"({x},{y}) t{triangle}";

    /// <summary>
    /// Why the engine view's way to one of <paramref name="offsets"/> from the line's
    /// <paramref name="slots"/> is not one the player can take, when the audit's own model can
    /// say: every way to it needs guards that contradict each other; or runs a companion's own
    /// copy through a party request to slot 1 or 2; or tests a value the line itself sets
    /// first, which nothing but other events the player starts ever writes. Null when none
    /// holds, and the difference is reported. The catalog's reasons are not consulted.
    /// </summary>
    private string? ExplainExclusion(int entity, IReadOnlyCollection<int> offsets, int[] slots)
    {
        var effects = slots
            .SelectMany(slot => closures.TryGetValue($"e{entity}.s{slot}", out var closure) ? closure.Effects : [])
            .Where(effect => offsets.Contains(effect.Effect.At))
            .ToArray();
        if (effects.Length == 0)
        {
            return null;
        }

        if (effects.All(effect => GuardsContradict(effect.Must)))
        {
            return "guards contradict each other";
        }

        if (effects.All(ReachedThroughCompanionRequest))
        {
            return "a companion's own copy, through a party request to slot 1 or 2";
        }

        if (effects.All(MovedAgainByALaterRequest))
        {
            return "the routine asks the same model to move again afterwards";
        }

        if (ConstantFolding(entity, slots, offsets) is { } folding &&
            folding["otherWriters"]!.AsArray().Select(node => node!.GetValue<string>()).All(IsPlayerStartedEvent) &&
            folding["kind"]!.GetValue<string>() == "another script writes it")
        {
            return "a value the line sets itself decides it; only events the player starts write it otherwise";
        }

        return null;
    }

    private bool IsPlayerStartedEvent(string entryId) =>
        a.EntriesById.TryGetValue(entryId, out var entry) &&
        entry.EngineTriggered &&
        entry.Kind is not (EntryKind.Init or EntryKind.Main);

    /// <summary>Whether no byte value satisfies every comparison the way needs on some one variable.</summary>
    private bool GuardsContradict(IReadOnlyList<Literal> must)
    {
        var tests = new Dictionary<string, List<(int Operator, int Value, bool Holds)>>(StringComparer.Ordinal);
        foreach (var literal in must)
        {
            if (!a.Conditions.TryGetValue(literal.At, out var condition) ||
                condition.Kind != ConditionKind.Compare ||
                condition.Left?.Variable is not { Width: 1 } variable ||
                condition.Right is not { IsImmediate: true } right)
            {
                continue;
            }

            if (!tests.TryGetValue(variable.Key, out var list))
            {
                list = [];
                tests[variable.Key] = list;
            }

            list.Add((condition.Operator, right.Raw, literal.Holds));
        }

        foreach (var list in tests.Values)
        {
            if (list.Count < 2)
            {
                continue;
            }

            var satisfiable = Enumerable.Range(0, 256).Any(value =>
                list.All(test => Evaluate(test.Operator, value, test.Value) is not { } holds || holds == test.Holds));
            if (!satisfiable)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the script that asked for the movement's script goes on to ask the same
    /// entity for another script that moves it, after the request on every way on: then the
    /// movement is where the model passes in the routine, not where it stops, which the
    /// audit's per-script check (IsIntermediateMovement) cannot see.
    /// </summary>
    private bool MovedAgainByALaterRequest(ClosureEffect effect)
    {
        if (effect.Chain.Count < 2 || !a.EntriesById.TryGetValue(effect.Chain[^1], out var callee))
        {
            return false;
        }

        var caller = effect.Chain[^2];
        var mover = callee.Entity;
        var sites = a.CallSites.Where(site => site.FromEntry == caller && site.Targets.Contains(callee.Id)).ToArray();
        if (sites.Length == 0)
        {
            return false;
        }

        bool MovesMover(string target) =>
            a.EntriesById.TryGetValue(target, out var entry) && entry.Entity == mover &&
            entry.Reach.Any(at => a.Flow.Instructions.TryGetValue(at, out var instruction) &&
                                  instruction.Op is 0xC0 or 0xC2 or Opcodes.XYZI);

        var laterMoves = a.CallSites
            .Where(site => site.Targets.Any(MovesMover))
            .Select(site => site.At)
            .ToHashSet();
        foreach (var site in sites)
        {
            // Every way on from the request reaches another request that moves the model
            // before the caller can end.
            var pending = new Stack<int>(a.Flow.Successors(site.At));
            var seen = new HashSet<int>();
            var always = pending.Count > 0;
            while (pending.Count != 0 && always)
            {
                var at = pending.Pop();
                if (!seen.Add(at) || laterMoves.Contains(at))
                {
                    continue;
                }

                var next = a.Flow.Successors(at);
                if (next.Count == 0 || seen.Count > 4096 ||
                    (a.Flow.Instructions.TryGetValue(at, out var instruction) && instruction.Op == Opcodes.MAPJUMP))
                {
                    always = false;
                    break;
                }

                foreach (var successor in next)
                {
                    pending.Push(successor);
                }
            }

            if (!always)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the way to the effect enters the script that runs it through a party request
    /// to slot 1 or 2, so the model it moves is a companion's, never the one being led.
    /// </summary>
    private bool ReachedThroughCompanionRequest(ClosureEffect effect)
    {
        for (var index = 1; index < effect.Chain.Count; index++)
        {
            var from = effect.Chain[index - 1];
            var to = effect.Chain[index];
            var sites = a.CallSites.Where(site => site.FromEntry == from && site.Targets.Contains(to)).ToArray();
            if (sites.Length > 0 && sites.All(site => site.Call.IsParty && site.Call.Target is 1 or 2))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Where a constant JUMP, LADER or XYZI puts its model, in the catalog's landing terms.</summary>
    private string? EngineLandingKey(int at)
    {
        if (!a.Flow.Instructions.TryGetValue(at, out var instruction) || instruction.Bytes is not { Length: >= 9 } b ||
            b[1] != 0 || b[2] != 0)
        {
            return null;
        }

        return instruction.Op switch
        {
            0xC0 => LandingKey(BitConverter.ToInt16(b, 3), BitConverter.ToInt16(b, 5), BitConverter.ToUInt16(b, 7)),
            0xC2 or Opcodes.XYZI when b.Length >= 11 =>
                LandingKey(BitConverter.ToInt16(b, 3), BitConverter.ToInt16(b, 5), BitConverter.ToUInt16(b, 9)),
            _ => null
        };
    }

    /// <summary>The entity whose script runs the instruction at <paramref name="at"/>, or -1 when no entry owns it.</summary>
    private int MoverOf(int at) =>
        a.Entries.FirstOrDefault(entry => entry.Reach.Contains(at))?.Entity ?? -1;

    /// <summary>
    /// The audit's own reading of which models can ever be the one the player moves: field
    /// setup controls model 0, the first model an Init loads; PC in Init binds a party
    /// character's entity; and CC names any entity. Worked out from the audit's decode, not
    /// from the catalog's.
    /// </summary>
    private HashSet<int> ControllableEntities()
    {
        var result = a.Entities
            .Where(entity => entity.Kind == EntityKind.PartyCharacter)
            .Select(entity => entity.Index)
            .ToHashSet();
        if (a.Entities.FirstOrDefault(entity => entity.ModelIds.Count > 0) is { } first)
        {
            result.Add(first.Index);
        }

        foreach (var at in liveInstructions)
        {
            if (a.Flow.Instructions.TryGetValue(at, out var instruction) && instruction.Op == 0xBF && instruction.Bytes.Length >= 2)
            {
                result.Add(instruction.Bytes[1]);
            }
        }

        return result;
    }

    /// <summary>
    /// Whether every way on from the movement at <paramref name="at"/> moves the model again
    /// before its script can end, so it is where the model passes rather than where it stops.
    /// A way that returns, stops, leaves the field or leaves the decoded code ends there.
    /// </summary>
    private bool IsIntermediateMovement(int at)
    {
        var pending = new Stack<int>(a.Flow.Successors(at));
        if (pending.Count == 0)
        {
            return false;
        }

        var seen = new HashSet<int>();
        while (pending.Count != 0)
        {
            var offset = pending.Pop();
            if (!seen.Add(offset))
            {
                continue;
            }

            if (seen.Count > 4096 || !a.Flow.Instructions.TryGetValue(offset, out var instruction))
            {
                return false;
            }

            if (instruction.Op is 0xC0 or 0xC2 or Opcodes.XYZI)
            {
                continue;
            }

            var successors = a.Flow.Successors(offset);
            if (instruction.Op == Opcodes.MAPJUMP || successors.Count == 0)
            {
                return false;
            }

            foreach (var successor in successors)
            {
                pending.Push(successor);
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the way to one of <paramref name="offsets"/> needs a test on a variable the
    /// same chain first sets to a constant that fails the test, while some other entry
    /// writes the variable. The shipping walker folds such a constant and drops the
    /// branch; the engine runs scripts concurrently (REQ is asynchronous), so another
    /// entity's write can still make the test hold.
    /// </summary>
    private JsonObject? ConstantFolding(int entity, int[] slots, IReadOnlyCollection<int> offsets)
    {
        foreach (var slot in slots)
        {
            foreach (var effect in closures[$"e{entity}.s{slot}"].Effects.Where(effect => offsets.Contains(effect.Effect.At)))
            {
                var chain = effect.Chain.Select(id => a.EntriesById[id]).ToArray();
                foreach (var literal in effect.Must)
                {
                    if (!a.Conditions.TryGetValue(literal.At, out var condition) ||
                        condition.Kind != ConditionKind.Compare ||
                        condition.Left?.Variable is not { Width: 1 } variable ||
                        condition.Right is not { IsImmediate: true } right)
                    {
                        continue;
                    }

                    foreach (var entry in chain)
                    {
                        foreach (var at in entry.Reach)
                        {
                            var instruction = a.Flow.Instructions[at];
                            if (instruction.Op != Opcodes.SETBYTE)
                            {
                                continue;
                            }

                            var operands = Semantics.Operands(instruction);
                            if (operands.Count < 2 || operands[0].Variable?.Key != variable.Key || !operands[1].IsImmediate)
                            {
                                continue;
                            }

                            var folded = Evaluate(condition.Operator, operands[1].Raw, right.Raw);
                            if (folded is null || folded == literal.Holds)
                            {
                                continue;
                            }

                            // The constant has to be set before the test on the way there.
                            var setIndex = System.Array.IndexOf(chain, entry);
                            var testIndex = System.Array.FindIndex(chain, candidate => candidate.Reach.Contains(literal.At));
                            var precedes = setIndex < testIndex ||
                                           (setIndex == testIndex && a.Flow.Reach(instruction.End).Contains(literal.At));
                            if (testIndex < 0 || !precedes)
                            {
                                continue;
                            }

                            // Slots that share a chain script's pointer run the same code, so they
                            // are not another writer.
                            var chainOffsets = chain.Select(item => item.Offset).ToHashSet();
                            var otherWriters = a.Entries
                                .Where(other => other.Live && !chainOffsets.Contains(other.Offset))
                                .Where(other => other.Reach.Any(point => Semantics.Writes(a.Flow.Instructions[point])
                                    .Any(write => write.Variable?.Key == variable.Key)))
                                .Select(other => other.Id)
                                .Take(8)
                                .ToArray();
                            // The walker only forgets a folded constant after ASK; any other write
                            // to the variable along the same chain leaves it stale.
                            var laterWrites = chain
                                .SelectMany(item => item.Reach)
                                .Where(point => point != at && a.Flow.Instructions[point].Op != Opcodes.ASK &&
                                                Semantics.Writes(a.Flow.Instructions[point]).Any(write => write.Variable?.Key == variable.Key) &&
                                                !(a.Flow.Instructions[point].Op == Opcodes.SETBYTE &&
                                                  Semantics.Operands(a.Flow.Instructions[point]) is { Count: >= 2 } setOperands &&
                                                  setOperands[1].IsImmediate))
                                .Distinct()
                                .Take(8)
                                .ToArray();
                            if (otherWriters.Length == 0 && laterWrites.Length == 0)
                            {
                                continue;
                            }

                            return new JsonObject
                            {
                                ["kind"] = otherWriters.Length > 0 ? "another script writes it" : "a later write on the same chain is not seen",
                                ["laterWritesInChain"] = Array(laterWrites.Select(point => $"{point}:{a.Flow.Instructions[point].Name}")),
                                ["variable"] = variable.ToString(),
                                ["setAt"] = at,
                                ["setIn"] = entry.Id,
                                ["setValue"] = operands[1].Raw,
                                ["test"] = a.Describe(literal),
                                ["testAt"] = literal.At,
                                ["otherWriters"] = Array(otherWriters)
                            };
                        }
                    }
                }
            }
        }

        return null;
    }

    private static bool? Evaluate(int op, int left, int right) => op switch
    {
        0 => left == right,
        1 => left != right,
        2 => left > right,
        3 => left < right,
        4 => left >= right,
        5 => left <= right,
        _ => null
    };

    private IEnumerable<string> EventSlotAliases(int entity) =>
        Enumerable.Range(1, 6)
            .Where(slot => a.Section.Entities[entity].Slots[slot].AliasOf is not null)
            .Select(slot => $"s{slot}->s{a.Section.Entities[entity].Slots[slot].AliasOf}");

    private JsonObject WalkerWitness(int entity, JsonObject item, int[] slots)
    {
        var chains = new JsonArray();
        var offsets = item["engineAt"]!.AsArray().Select(node => node!.GetValue<int>()).ToHashSet();
        foreach (var slot in slots)
        {
            if (!closures.TryGetValue($"e{entity}.s{slot}", out var closure))
            {
                continue;
            }

            foreach (var effect in closure.Effects.Where(effect => offsets.Contains(effect.Effect.At)))
            {
                chains.Add(new JsonObject
                {
                    ["root"] = $"e{entity}.s{slot}",
                    ["at"] = effect.Effect.At,
                    ["bytes"] = Bytes(effect.Effect.At, 12),
                    ["via"] = Array(effect.Chain),
                    ["must"] = Guards(effect.Must)
                });
                break;
            }
        }

        var witness = (JsonObject)item.DeepClone();
        witness["entity"] = entity;
        witness["engine"] = chains;
        witness["repro"] = Repro;
        return witness;
    }

    /// <summary>
    /// The navigation instructions each of <paramref name="slots"/> can run, by signature,
    /// with their offsets. Variable arguments cannot be compared and are kept apart.
    /// </summary>
    private Dictionary<string, List<int>> EngineNavigationSignatures(int entity, int[] slots, bool dynamic = false)
    {
        var result = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            if (!closures.TryGetValue($"e{entity}.s{slot}", out var closure))
            {
                continue;
            }

            foreach (var effect in closure.Effects)
            {
                if (effect.Dynamic != dynamic || NavigationSignature(effect.Effect.At) is not { } signature)
                {
                    continue;
                }

                if (!result.TryGetValue(signature, out var offsets))
                {
                    offsets = [];
                    result[signature] = offsets;
                }

                if (!offsets.Contains(effect.Effect.At))
                {
                    offsets.Add(effect.Effect.At);
                }
            }
        }

        return result;
    }

    private IEnumerable<int> EngineInstructionsWithSignature(string signature) =>
        a.Flow.Instructions.Keys.Where(at => NavigationSignature(at) == signature).Order();

    /// <summary>What a navigation instruction does, in the shipping action's terms.</summary>
    private string? NavigationSignature(int at)
    {
        if (!a.Flow.Instructions.TryGetValue(at, out var instruction))
        {
            return null;
        }

        var b = instruction.Bytes;
        var constant = b.Length >= 3 && b[1] == 0 && b[2] == 0;
        return instruction.Op switch
        {
            Opcodes.MAPJUMP => $"MapJump {BitConverter.ToUInt16(b, 1)}",
            0xC2 when constant => $"Ladder ({BitConverter.ToInt16(b, 3)},{BitConverter.ToInt16(b, 5)},{BitConverter.ToInt16(b, 7)}) {BitConverter.ToUInt16(b, 9)}",
            0xC0 when constant => $"Jump ({BitConverter.ToInt16(b, 3)},{BitConverter.ToInt16(b, 5)}) {BitConverter.ToUInt16(b, 7)}",
            Opcodes.XYZI when constant && b.Length >= 11 => $"Place ({BitConverter.ToInt16(b, 3)},{BitConverter.ToInt16(b, 5)},{BitConverter.ToInt16(b, 7)}) {BitConverter.ToUInt16(b, 9)}",
            0xC2 or 0xC0 or Opcodes.XYZI => $"dynamic {instruction.Name}@{at}",
            _ => null
        };
    }

    private static bool IsVerifiedActionActivatedExitScript(int fieldId, int entityId, int scriptId) =>
        (fieldId == 238 && entityId is >= 14 and <= 17 && scriptId == 1) ||
        (fieldId == 518 && entityId == 11 && scriptId == 1);

    private bool IsNavigation(int at) =>
        a.Flow.Instructions.TryGetValue(at, out var instruction) && NavigationOpcodes.Contains(instruction.Op);

    private string Describe(int at)
    {
        if (!a.Flow.Instructions.TryGetValue(at, out var instruction))
        {
            return $"{at}:?";
        }

        return instruction.Op switch
        {
            Opcodes.MAPJUMP => $"{at}:MAPJUMP {BitConverter.ToUInt16(instruction.Bytes, 1)}",
            0xC2 => $"{at}:LADER tri {BitConverter.ToUInt16(instruction.Bytes, 9)}",
            0xC0 => $"{at}:JUMP tri {BitConverter.ToUInt16(instruction.Bytes, 7)}",
            Opcodes.XYZI => $"{at}:XYZI tri {BitConverter.ToUInt16(instruction.Bytes, 9)}",
            _ => $"{at}:{instruction.Name}"
        };
    }

    private JsonArray NpcJson(IReadOnlyList<EntryModel> roots)
    {
        var array = new JsonArray();
        var shippingNpcs = shipping?.Npcs ?? [];
        var byEntity = shippingNpcs.GroupBy(npc => npc.EntityId).ToDictionary(group => group.Key, group => group.First());
        foreach (var entity in a.Entities.Where(entity => entity.Kind is EntityKind.Model or EntityKind.PartyCharacter))
        {
            var talkId = $"e{entity.Index}.s1";
            closures.TryGetValue(talkId, out var talk);
            var talkEffects = talk.Effects ?? [];
            var perceivable = talkEffects.Where(effect => PerceivableKinds.Contains(effect.Effect.Kind)).ToArray();
            var engineDialogs = talkEffects
                .Where(effect => effect.Effect.Kind is "Dialog" or "Ask")
                .Select(effect => DialogId(effect.Effect.At))
                .Where(id => id >= 0)
                .Distinct()
                .Order()
                .ToArray();
            var state = TalkState(entity.Index);
            var coveredBy = CoveringRows(entity.Index);
            byEntity.TryGetValue(entity.Index, out var npc);
            var inShipping = byEntity.ContainsKey(entity.Index);

            // Somebody the catalog reads as walked into (ContactOnly) says what Contact shows, so
            // that person's dialogue is checked against the engine's Contact script, e<n>.s2, which
            // this oracle walks for itself.
            var byContact = inShipping && npc.ContactOnly;
            closures.TryGetValue($"e{entity.Index}.s2", out var contactClosure);
            var interactionEffects = byContact ? contactClosure.Effects ?? [] : talkEffects;
            var interactionPerceivable = interactionEffects.Where(effect => PerceivableKinds.Contains(effect.Effect.Kind)).ToArray();
            if (byContact)
            {
                engineDialogs = interactionEffects
                    .Where(effect => effect.Effect.Kind is "Dialog" or "Ask")
                    .Select(effect => DialogId(effect.Effect.At))
                    .Where(id => id >= 0)
                    .Distinct()
                    .Order()
                    .ToArray();
            }

            var label = inShipping && context.Text is not null ? ShippingLabels.Resolve(a.FieldId, npc, context.Text) : null;
            var shippingDialogs = inShipping ? npc.DialogIds.Distinct().Order().ToArray() : [];
            var missingDialogs = engineDialogs.Where(id => !shippingDialogs.Contains(id)).ToArray();
            var extraDialogs = shippingDialogs.Where(id => !engineDialogs.Contains(id)).ToArray();
            global.Count("npc.modelEntities");
            if (perceivable.Length > 0)
            {
                global.Count("npc.engineTalkPerceivable");
            }

            if (inShipping)
            {
                global.Count("npc.shipping");
                if (label is { Length: 0 })
                {
                    global.Count("npc.shippingEmptyLabel");
                }
            }

            var witness = new JsonObject
            {
                ["entity"] = entity.Index,
                ["name"] = entity.Name,
                ["kind"] = entity.Kind.ToString(),
                ["partyCharacter"] = entity.PartyCharacters.Count > 0 ? Array(entity.PartyCharacters) : null,
                ["talk"] = talkId,
                ["talkPointer"] = a.Section.Entities[entity.Index].Pointers[1],
                ["perceivable"] = Array(perceivable.Select(effect => $"{effect.Effect.Kind} {effect.Effect.Detail}").Distinct().Take(12)),
                ["engineDialogs"] = Array(engineDialogs),
                ["dialogText"] = context.IncludeText ? Array(engineDialogs.Take(4).Select(id => FirstLine(id))) : null,
                ["talkState"] = state.Json,
                ["interaction"] = byContact ? "contact" : "talk",
                ["coveredBy"] = coveredBy.Length == 0 ? null : Array(coveredBy),
                ["shipping"] = inShipping ? new JsonObject
                {
                    ["dialogs"] = Array(shippingDialogs),
                    ["label"] = label,
                    ["line"] = npc.InteractionLineEntityId
                } : null,
                ["repro"] = Repro
            };
            if (perceivable.Length > 0 && !inShipping && state.EverTalkable && coveredBy.Length == 0)
            {
                AddCandidate("NpcTalkNotCatalogued", 1,
                    $"{entity.Name} (entity {entity.Index}): Talk runs {string.Join(", ", perceivable.Select(effect => effect.Effect.Kind).Distinct())}; not in the shipping NPC list",
                    witness);
            }

            if (perceivable.Length > 0 && state.EverTalkable && !state.EverVisible && coveredBy.Length == 0)
            {
                AddCandidate("NpcTalkWhileInvisible", 1,
                    $"{entity.Name} (entity {entity.Index}): Talk is enabled and perceivable but the model is never shown; the runtime NPC reader skips invisible models",
                    witness);
            }

            if (inShipping && label is not null && label.Length == 0 && coveredBy.Length == 0 && perceivable.Length > 0)
            {
                AddCandidate("NpcEmptyShippingLabel", 2,
                    $"{entity.Name} (entity {entity.Index}): catalogued, but the shipping reader resolves no label and drops it",
                    witness);
            }

            if (inShipping && (missingDialogs.Length > 0 || extraDialogs.Length > 0))
            {
                witness["missingDialogs"] = Array(missingDialogs);
                witness["extraDialogs"] = Array(extraDialogs);
                var withoutPartyMisread = ShippingDialogsWithoutPartyMisread(entity.Index);
                var partyDialogs = interactionEffects.Where(effect => effect.Dynamic && effect.Effect.Kind is "Dialog" or "Ask")
                    .Select(effect => DialogId(effect.Effect.At)).ToHashSet();
                witness["extraFromPartyMisread"] = Array(extraDialogs.Where(id => !withoutPartyMisread.Contains(id)));
                witness["extraFromDeadCode"] = Array(extraDialogs.Where(withoutPartyMisread.Contains));
                witness["missingPartyLines"] = Array(missingDialogs.Where(partyDialogs.Contains));
                witness["missingOther"] = Array(missingDialogs.Where(id => !partyDialogs.Contains(id)));
                global.Count("npcDialogs.extraFromPartyMisread", extraDialogs.Count(id => !withoutPartyMisread.Contains(id)));
                global.Count("npcDialogs.extraFromDeadCode", extraDialogs.Count(withoutPartyMisread.Contains));
                global.Count("npcDialogs.missingPartyLines", missingDialogs.Count(partyDialogs.Contains));
                global.Count("npcDialogs.missingOther", missingDialogs.Count(id => !partyDialogs.Contains(id)));
                AddCandidate("NpcDialogSetDiffers", missingDialogs.Length > 0 ? 2 : 3,
                    $"{entity.Name} (entity {entity.Index}): engine {(byContact ? "Contact" : "Talk")} dialogs {string.Join(",", engineDialogs)}; shipping {string.Join(",", shippingDialogs)}",
                    witness);
            }

            if (interactionPerceivable.Length == 0 && inShipping && shippingDialogs.Length > 0)
            {
                AddCandidate("NpcDialogUnreachable", 3,
                    $"{entity.Name} (entity {entity.Index}): shipping lists dialogs {string.Join(",", shippingDialogs)} but the engine {(byContact ? "Contact" : "Talk")} runs none",
                    witness);
            }

            var contactId = $"e{entity.Index}.s2";
            if (closures.TryGetValue(contactId, out var contact) &&
                contact.Effects.Any(effect => PerceivableKinds.Contains(effect.Effect.Kind)) &&
                a.Section.Entities[entity.Index].Pointers[2] != a.Section.Entities[entity.Index].Pointers[1])
            {
                global.Count("npc.contactPerceivable");
                AddCandidate("ContactRunsPerceivableScript", 2,
                    $"{entity.Name} (entity {entity.Index}): touching it runs {string.Join(", ", contact.Effects.Where(effect => PerceivableKinds.Contains(effect.Effect.Kind)).Select(effect => effect.Effect.Kind).Distinct())}",
                    new JsonObject
                    {
                        ["entity"] = entity.Index,
                        ["contact"] = contactId,
                        ["shippingContactNpc"] = shipping?.Npcs.Any(npc => npc.EntityId == entity.Index && npc.ContactOnly) == true,
                        ["effects"] = Array(contact.Effects.Where(effect => PerceivableKinds.Contains(effect.Effect.Kind)).Select(effect => $"{effect.Effect.At}:{effect.Effect.Kind} {effect.Effect.Detail} if {string.Join(" and ", effect.Must.Select(a.Describe))}").Take(8)),
                        ["repro"] = Repro
                    });
            }

            array.Add(witness);
        }

        return array;
    }

    /// <summary>
    /// Every shipping reader that offers this entity to the player: an NPC definition, an
    /// Object or Story row, or a script exit on its line.
    /// </summary>
    private string[] OfferedBy(int entity)
    {
        var offered = new List<string>(CoveringRows(entity));
        if (shipping?.Npcs.Any(npc => npc.EntityId == entity || npc.InteractionLineEntityId == entity) == true)
        {
            offered.Add("npc");
        }

        if (shipping?.Exits.Any(exit => exit.TriggerEntityId == entity) == true)
        {
            offered.Add("script exit");
        }

        return offered.Distinct().ToArray();
    }

    /// <summary>
    /// Exits the shipping catalog finishes on a triangle rather than on a LINE (the Gold Saucer
    /// platforms and the reviewed polled rooms) that lead where this MAPJUMP does. Whether the
    /// triangle is the one the Main polls is the ledger's review, not this.
    /// </summary>
    private IEnumerable<string> TriangleExitsTo(int destination) =>
        shipping?.Exits
            .Where(exit => exit.CompletionTriangles is { Count: > 0 } && exit.DestinationFieldIds?.Contains(destination) == true)
            .Select(exit => $"triangle exit {exit.StableId}") ?? [];

    /// <summary>
    /// Story Location rows finished on the one triangle this MAPJUMP's own tests hold the party
    /// to - a temporary compared == with a constant, which GETAI has filled with a party
    /// character's triangle on the same chain - and offered only inside the GameMoment bounds
    /// those tests give (the ujunon2 dolphin). Both are read from the native tests, not the
    /// row: a row on another triangle or with a wider band is not listed. A key or a choice on
    /// the way is left to the player and to the row's label.
    /// </summary>
    private IEnumerable<string> StoryRowsOnThePolledTriangle(ClosureEffect effect)
    {
        var leaderTriangles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in effect.Chain)
        {
            if (!a.EntriesById.TryGetValue(id, out var entry))
            {
                continue;
            }

            foreach (var at in entry.Reach)
            {
                if (a.Flow.Instructions.TryGetValue(at, out var instruction) && instruction.Op == 0xB9 && instruction.Length >= 4 &&
                    instruction.Bytes[2] < a.Entities.Count && a.Entities[instruction.Bytes[2]].Kind == EntityKind.PartyCharacter)
                {
                    leaderTriangles.UnionWith(Semantics.Writes(instruction)
                        .Where(write => write.Variable is { IsSavemap: false })
                        .Select(write => write.Variable!.Key));
                }
            }
        }

        var tests = effect.Must
            .Where(literal => a.Conditions.ContainsKey(literal.At))
            .Select(literal => (Condition: a.Conditions[literal.At], literal.Holds))
            .Where(test => test.Condition.Kind == ConditionKind.Compare && test.Condition.Right is { IsImmediate: true })
            .ToArray();
        var triangles = tests
            .Where(test => test.Holds && test.Condition.Operator == 0 &&
                           test.Condition.Left?.Variable is { } variable && leaderTriangles.Contains(variable.Key))
            .Select(test => test.Condition.Right!.Raw)
            .ToHashSet();
        if (triangles.Count != 1)
        {
            yield break;
        }

        var (low, high) = (int.MinValue, int.MaxValue);
        foreach (var (condition, holds) in tests)
        {
            if (condition.Left?.Variable is not { Block: "1", Address: 0, Width: 2 })
            {
                continue;
            }

            var value = condition.Right!.Raw;
            switch (holds ? condition.Operator : condition.Operator switch { 2 => 5, 3 => 4, 4 => 3, 5 => 2, 1 => 0, _ => -1 })
            {
                case 0: (low, high) = (Math.Max(low, value), Math.Min(high, value)); break;
                case 2: low = Math.Max(low, value + 1); break;
                case 3: high = Math.Min(high, value - 1); break;
                case 4: low = Math.Max(low, value); break;
                case 5: high = Math.Min(high, value); break;
            }
        }

        foreach (var row in context.Story[a.FieldId])
        {
            if (row.Kind == FieldStoryTargetKind.Location && row.CompletionPlayerTriangles is { Length: > 0 } rowTriangles &&
                rowTriangles.All(triangles.Contains) &&
                (row.MinimumGameMoment < 0 ? int.MinValue : row.MinimumGameMoment) >= low &&
                (row.MaximumGameMoment < 0 ? int.MaxValue : row.MaximumGameMoment) <= high)
            {
                yield return $"story triangle row: {row.Label}";
            }
        }
    }

    /// <summary>
    /// The shipping Talk collector (CollectTalkDialogIds) with one change: PREQ/PRQSW/PRQEW
    /// are not read as entity numbers. Whatever the real collector finds beyond this came
    /// from reading a party slot as an entity.
    /// </summary>
    private HashSet<int> ShippingDialogsWithoutPartyMisread(int entity)
    {
        var dialogs = new HashSet<int>();
        var visited = new HashSet<(int, int)>();
        void Collect(int group, int slot)
        {
            if (!view!.HasScript(group, slot) || !visited.Add((group, slot)))
            {
                return;
            }

            foreach (var opcode in view.Opcodes(group, slot))
            {
                if (opcode.Id is >= 0x01 and <= 0x03)
                {
                    Collect(opcode.Bytes[1], opcode.Bytes[2] & 0x1F);
                }
                else if (opcode.Id == 0x40 && opcode.Bytes.Length >= 3)
                {
                    dialogs.Add(opcode.Bytes[2]);
                }
                else if (opcode.Id == 0x48 && opcode.Bytes.Length >= 4)
                {
                    dialogs.Add(opcode.Bytes[3]);
                }
            }
        }

        if (view is not null)
        {
            Collect(entity, 1);
        }

        return dialogs;
    }

    /// <summary>The Object and Story rows that already offer this entity, by their labels.</summary>
    private string[] CoveringRows(int entity) =>
        context.Objects[a.FieldId].Where(row => row.EntityId == entity)
            .Select(row => $"object: {row.Label ?? row.Kind.ToString()}")
            .Concat(context.Story[a.FieldId]
                .Where(row => row.EntityId == entity && row.Kind == FieldStoryTargetKind.Model)
                .Select(row => $"story: {row.Label}"))
            .Distinct()
            .ToArray();

    /// <summary>
    /// Whether the entity's own scripts ever leave its Talk enabled and its model shown. The
    /// engine defaults are taken as enabled and shown (TLKON and VISI are only needed to
    /// change them); an unconditional <c>TLKON 1</c> or <c>VISI 0</c> in Init that no live
    /// script reverses is the only thing that makes the answer no.
    /// </summary>
    private (bool EverTalkable, bool EverVisible, JsonObject Json) TalkState(int entity)
    {
        var talkOffInInit = false;
        var talkOnAnywhere = false;
        var hiddenInInit = false;
        var shownAnywhere = false;
        var talkOps = new JsonArray();
        foreach (var entry in a.Entries.Where(entry => entry.Entity == entity && entry.Live))
        {
            foreach (var at in entry.Reach)
            {
                if (!a.Flow.Instructions.TryGetValue(at, out var instruction))
                {
                    continue;
                }

                var unconditional = entry.Must[at].Count == 0 && entry.May[at].Count == 0;
                switch (instruction.Op)
                {
                    case Opcodes.TLKON:
                        if (instruction.Bytes[1] == 0)
                        {
                            talkOnAnywhere = true;
                        }
                        else if (entry.Kind == EntryKind.Init && unconditional)
                        {
                            talkOffInInit = true;
                        }

                        talkOps.Add($"{entry.Id}@{at} TLKON {instruction.Bytes[1]}{(unconditional ? string.Empty : " (guarded)")}");
                        break;
                    case Opcodes.VISI:
                        if (instruction.Bytes[1] != 0)
                        {
                            shownAnywhere = true;
                        }
                        else if (entry.Kind == EntryKind.Init && unconditional)
                        {
                            hiddenInInit = true;
                        }

                        talkOps.Add($"{entry.Id}@{at} VISI {instruction.Bytes[1]}{(unconditional ? string.Empty : " (guarded)")}");
                        break;
                }
            }
        }

        var everTalkable = !talkOffInInit || talkOnAnywhere;
        var everVisible = !hiddenInInit || shownAnywhere;
        return (everTalkable, everVisible, new JsonObject
        {
            ["everTalkable"] = everTalkable,
            ["everVisible"] = everVisible,
            ["ops"] = talkOps
        });
    }

    private int DialogId(int at)
    {
        if (!a.Flow.Instructions.TryGetValue(at, out var instruction))
        {
            return -1;
        }

        return instruction.Op switch
        {
            Opcodes.MESSAGE => instruction.Bytes[2],
            Opcodes.ASK => instruction.Bytes[3],
            _ => -1
        };
    }

    private string FirstLine(int dialogId)
    {
        try
        {
            var lines = context.Text?.ReadMessageLinesById(a.FieldId, dialogId) ?? [];
            var text = lines.Count == 0 ? string.Empty : string.Join(" / ", lines.Take(2));
            return $"{dialogId}: {(text.Length > 120 ? text[..120] + "..." : text)}";
        }
        catch (Exception exception)
        {
            return $"{dialogId}: <{exception.GetType().Name}>";
        }
    }

    private JsonArray PickupJson(IReadOnlyList<EntryModel> roots)
    {
        var array = new JsonArray();
        var rows = context.Objects[a.FieldId].ToArray();
        foreach (var root in roots)
        {
            foreach (var effect in closures[root.Id].Effects.Where(effect => PickupKinds.Contains(effect.Effect.Kind)))
            {
                var trigger = Trigger(root, effect);
                var executing = a.EntriesById[effect.EntryId].Entity;
                var hasRow = rows.Any(row => row.EntityId == root.Entity || row.EntityId == executing);
                global.Count($"pickup.{trigger}");
                if (hasRow)
                {
                    global.Count("pickup.withObjectRow");
                }

                var offeredBy = OfferedBy(root.Entity);
                var item = new JsonObject
                {
                    ["root"] = root.Id,
                    ["entityName"] = a.Entities[root.Entity].Name,
                    ["at"] = effect.Effect.At,
                    ["kind"] = effect.Effect.Kind,
                    ["detail"] = effect.Effect.Detail,
                    ["trigger"] = trigger,
                    ["via"] = Array(effect.Chain),
                    ["must"] = Guards(effect.Must),
                    ["objectRow"] = hasRow,
                    ["offeredBy"] = Array(offeredBy)
                };
                array.Add(item);
                if (!hasRow && offeredBy.Length == 0 && IsPlayerTrigger(trigger))
                {
                    AddCandidate("PickupWithoutObjectRow", trigger.StartsWith("line", StringComparison.Ordinal) ? 3 : 2,
                        $"{a.Entities[root.Entity].Name} (entity {root.Entity}) {trigger}: {effect.Effect.Kind} {effect.Effect.Detail}; no object row for the entity",
                        new JsonObject
                        {
                            ["pickup"] = item.DeepClone(),
                            ["bytes"] = Bytes(effect.Effect.At, 8),
                            ["repro"] = Repro
                        });
                }
            }
        }

        return array;
    }

    private JsonObject ExitJson(IReadOnlyList<EntryModel> roots)
    {
        var shippingExits = shipping?.Exits ?? [];
        var shippingByEntity = new Dictionary<int, HashSet<int>>();
        foreach (var exit in shippingExits)
        {
            var entity = exit.TriggerEntityId;
            if (entity < 0)
            {
                continue;
            }

            if (!shippingByEntity.TryGetValue(entity, out var set))
            {
                set = [];
                shippingByEntity[entity] = set;
            }

            set.UnionWith(exit.DestinationFieldIds ?? []);
        }

        var jumps = new JsonArray();
        var seen = new HashSet<(string, int)>();
        var gatewayDestinations = (a.File.ReadTriggers()?.Gateways ?? []).Select(gateway => gateway.Destination).ToHashSet();
        foreach (var root in roots)
        {
            foreach (var effect in closures[root.Id].Effects.Where(effect => effect.Effect.Kind == "MapJump"))
            {
                if (!seen.Add((root.Id, effect.Effect.At)))
                {
                    continue;
                }

                var destination = BitConverter.ToUInt16(a.Flow.Instructions[effect.Effect.At].Bytes, 1);
                var trigger = Trigger(root, effect);
                var entityKind = a.Entities[root.Entity].Kind;
                var offeredAsScriptExit = shippingByEntity.TryGetValue(root.Entity, out var destinations) && destinations.Contains(destination);
                global.Count($"mapjump.{trigger}");
                if (destination == a.FieldId)
                {
                    global.Count("mapjump.selfField");
                }

                var item = new JsonObject
                {
                    ["root"] = root.Id,
                    ["entityName"] = a.Entities[root.Entity].Name,
                    ["entityKind"] = entityKind.ToString(),
                    ["at"] = effect.Effect.At,
                    ["destination"] = destination,
                    ["destinationName"] = global.FieldNames.GetValueOrDefault(destination) ?? context.Source?.FieldNames.GetValueOrDefault(destination),
                    ["trigger"] = trigger,
                    ["via"] = Array(effect.Chain),
                    ["must"] = Guards(effect.Must),
                    ["shippingScriptExit"] = offeredAsScriptExit
                };
                jumps.Add(item);
                if (destination == a.FieldId || offeredAsScriptExit || !IsPlayerTrigger(trigger))
                {
                    continue;
                }

                var offeredBy = OfferedBy(root.Entity).Concat(TriangleExitsTo(destination)).Concat(StoryRowsOnThePolledTriangle(effect)).ToArray();
                item["offeredBy"] = Array(offeredBy);
                if (trigger == "talk" && offeredBy.Length > 0)
                {
                    global.Count("mapjump.talkFromOfferedEntity");
                    continue;
                }

                if (entityKind == EntityKind.Line && trigger.StartsWith("line", StringComparison.Ordinal))
                {
                    // Compared action for action against the shipping walker in WalkerJson.
                    continue;
                }

                if (gatewayDestinations.Contains(destination))
                {
                    // The same field is already an Exits gateway here.
                    global.Count("mapjump.duplicatesGateway");
                    continue;
                }

                var (klass, priority) = trigger switch
                {
                    "talk" => ("TalkTransportNotOffered", 2),
                    "main-confirm" or "main-position" or "arrival+confirm" or "arrival+position" => ("PolledExitNotOffered", 2),
                    _ => ("OtherPlayerMapJump", 3)
                };
                AddCandidate(klass, priority,
                    $"{a.Entities[root.Entity].Name} (entity {root.Entity}) {trigger} -> field {destination}; not a shipping script exit",
                    new JsonObject
                    {
                        ["jump"] = item.DeepClone(),
                        ["bytes"] = Bytes(effect.Effect.At, 10),
                        ["repro"] = Repro
                    });
            }
        }

        var gatewayDisable = a.Entries
            .Where(entry => entry.Live)
            .SelectMany(entry => entry.Reach.Where(at => a.Flow.Instructions[at].Op == Opcodes.MPJPO)
                .Select(at => $"{entry.Id}@{at} MPJPO {a.Flow.Instructions[at].Bytes[1]} if {string.Join(" and ", entry.Must[at].Select(Literal.FromKey).Select(a.Describe))}"))
            .Distinct()
            .ToArray();
        if (gatewayDisable.Length > 0)
        {
            global.Count("field.usesMpjpo");
        }

        return new JsonObject
        {
            ["shippingScriptExits"] = new JsonArray(shippingExits.Select(exit => (JsonNode)new JsonObject
            {
                ["stableId"] = exit.StableId,
                ["entity"] = exit.TriggerEntityId,
                ["destinations"] = Array(exit.DestinationFieldIds ?? [])
            }).ToArray()),
            ["engineMapJumps"] = jumps,
            ["gatewayDisable"] = Array(gatewayDisable)
        };
    }

    /// <summary>
    /// Calls the shipping readers resolve differently from the engine: requests of an
    /// aliased slot (dropped), RETTO (neither followed nor treated as the end), and PREQ
    /// read as an entity number by the Talk dialogue collector and the counter-line test.
    /// </summary>
    private JsonArray CallFindingsJson()
    {
        var array = new JsonArray();
        var seenSites = new HashSet<int>();
        foreach (var site in a.CallSites)
        {
            var from = a.EntriesById[site.FromEntry];
            // A site is reached from every slot that shares its script's pointer; report it
            // once, with all of them.
            if (!from.Live || !seenSites.Add(site.At))
            {
                continue;
            }

            var liveFrom = a.CallSites
                .Where(other => other.At == site.At && a.EntriesById[other.FromEntry].Live)
                .Select(other => other.FromEntry)
                .ToArray();
            string? klass = null;
            if (site.Problem?.StartsWith("target slot aliases", StringComparison.Ordinal) == true)
            {
                klass = "RequestOfAliasedSlot";
            }
            else if (site.Call.Kind == CallKind.ReturnTo)
            {
                klass = "ReturnToScript";
            }
            else if (site.Call.IsParty && site.Call.Target < a.Section.EntityCount)
            {
                klass = "PartyRequestReadAsEntity";
            }

            if (klass is null)
            {
                continue;
            }

            // What the Talk collector would read if it took the party slot for an entity:
            // entity <slot>'s own script. The 0.6.8 collector did; the repaired one is held to
            // what it publishes, and a party request whose lines it does not list for the
            // entity is only a party request.
            int[]? misattributed = null;
            if (klass == "PartyRequestReadAsEntity" && view?.HasScript(site.Call.Target, site.Call.Script) == true)
            {
                misattributed = ShippingReflection.ReadOpcodes(view.Groups[site.Call.Target].Scripts[site.Call.Script])
                    .Where(opcode => opcode.Id is 0x40 or 0x48)
                    .Select(opcode => (int)(opcode.Id == 0x40 ? opcode.Bytes[2] : opcode.Bytes[3]))
                    .Distinct()
                    .ToArray();
            }

            if (klass == "PartyRequestReadAsEntity" && ShippingReflection.IsRepairedCatalog)
            {
                var listed = shipping?.Npcs.FirstOrDefault(npc => npc.EntityId == from.Entity).DialogIds ?? [];
                if (misattributed is null || !misattributed.Any(listed.Contains))
                {
                    klass = "PartyRequest";
                }
            }

            var targetEffects = site.Targets
                .Where(closures.ContainsKey)
                .SelectMany(target => closures[target].Effects)
                .Concat(site.Targets.Where(target => !closures.ContainsKey(target))
                    .SelectMany(target => a.Closure(a.EntriesById[target]).Effects))
                .Where(effect => PerceivableKinds.Contains(effect.Effect.Kind) || AccessKinds.Contains(effect.Effect.Kind) || IsNavigation(effect.Effect.At))
                .Select(effect => $"{effect.Effect.At}:{effect.Effect.Kind} {effect.Effect.Detail}")
                .Distinct()
                .Take(10)
                .ToArray();
            global.Count($"callFinding.{klass}");
            if (targetEffects.Length > 0)
            {
                global.Count($"callFinding.{klass}.withEffects");
            }

            var item = new JsonObject
            {
                ["class"] = klass,
                ["from"] = site.FromEntry,
                ["reachedFrom"] = Array(liveFrom),
                ["at"] = site.At,
                ["op"] = Opcodes.Name(a.Flow.Instructions[site.At].Op),
                ["bytes"] = Bytes(site.At, a.Flow.Instructions[site.At].Length),
                ["targets"] = Array(site.Targets),
                ["problem"] = site.Problem,
                ["targetEffects"] = Array(targetEffects),
                ["must"] = Guards(from.Must[site.At].Select(Literal.FromKey))
            };
            if (klass == "PartyRequestReadAsEntity")
            {
                item["shippingReads"] = $"e{site.Call.Target}.s{site.Call.Script}";
                if (misattributed is not null)
                {
                    item["misattributedDialogs"] = Array(misattributed);
                }
            }

            array.Add(item);
            // RETTO and requests of an aliased slot were deficiencies of the 0.6.8 walker,
            // which followed neither; the repaired catalog follows both, so for it they are
            // what the scripts do, not candidates.
            if (targetEffects.Length > 0 && klass is not ("PartyRequestReadAsEntity" or "PartyRequest") &&
                !ShippingReflection.IsRepairedCatalog)
            {
                AddCandidate(klass, 2,
                    $"{site.FromEntry} {Opcodes.Name(a.Flow.Instructions[site.At].Op)} {string.Join(",", site.Targets)}: {string.Join("; ", targetEffects.Take(3))}",
                    (JsonObject)item.DeepClone());
            }
        }

        return array;
    }

    private JsonArray StoryJson()
    {
        var array = new JsonArray();
        foreach (var row in context.Story[a.FieldId])
        {
            var keys = StoryKeys(row).Select(key => key.ToString()).Distinct().ToArray();
            var json = new JsonObject
            {
                ["label"] = row.Label,
                ["kind"] = row.Kind.ToString(),
                ["entity"] = row.EntityId,
                ["gameMoment"] = new JsonArray(row.MinimumGameMoment, row.TargetGameMoment, row.MaximumGameMoment),
                ["flags"] = Array(keys)
            };
            if (row.CompletionPlayerTriangles is { Length: > 0 } triangles)
            {
                json["completionTriangles"] = new JsonArray(triangles.Select(triangle => (JsonNode)triangle).ToArray());
            }

            array.Add(json);
        }

        return array;
    }

    /// <summary>The bits of its byte a test looks at: a mask for &amp;, one bit for bitON/bitOFF, all of it otherwise.</summary>
    private static int TestMask(Condition condition, VariableRef variable)
    {
        if (condition.Right is not { IsImmediate: true } right || !ReferenceEquals(condition.Left?.Variable, variable))
        {
            return 0xFF;
        }

        return condition.Operator switch
        {
            6 => right.Raw & 0xFF,
            9 or 10 when right.Raw is >= 0 and < 8 => 1 << right.Raw,
            _ => 0xFF
        };
    }

    public static IEnumerable<(ByteKey Key, int Mask)> StoryMasks(FieldStoryEventDefinition row)
    {
        var conditions = new List<FieldStoryStateCondition> { row.RequiredCondition, row.CompletedCondition };
        conditions.AddRange(row.RequiredConditions ?? []);
        foreach (var condition in conditions)
        {
            if (condition.PartyMemberId is not null || condition.Mask == 0 && condition.MinimumValue is null && condition.MaximumValue is null)
            {
                continue;
            }

            if (ByteKey.FromModBank(condition.Bank, condition.Address) is { } key)
            {
                yield return (key, condition.Mask != 0 ? condition.Mask : 0xFF);
            }
        }
    }

    public static IEnumerable<(ByteKey Key, int Mask)> ObjectMasks(FieldNavigationObjectDefinition row)
    {
        if (row.CollectedBank >= 0 && ByteKey.FromModBank(row.CollectedBank, row.CollectedAddress) is { } collected)
        {
            yield return (collected, row.CollectedMask != 0 ? row.CollectedMask : 0xFF);
        }

        if (row.RequiredBank >= 0 && ByteKey.FromModBank(row.RequiredBank, row.RequiredAddress) is { } required)
        {
            yield return (required, row.RequiredMask != 0 ? row.RequiredMask : 0xFF);
        }
    }

    public static IEnumerable<ByteKey> StoryKeys(FieldStoryEventDefinition row)
    {
        var conditions = new List<FieldStoryStateCondition> { row.RequiredCondition, row.CompletedCondition };
        conditions.AddRange(row.RequiredConditions ?? []);
        foreach (var condition in conditions)
        {
            if (condition.PartyMemberId is not null || condition.Mask == 0 && condition.MinimumValue is null && condition.MaximumValue is null)
            {
                continue;
            }

            if (ByteKey.FromModBank(condition.Bank, condition.Address) is { } key)
            {
                yield return key;
            }
        }
    }

    public static IEnumerable<ByteKey> ObjectKeys(FieldNavigationObjectDefinition row)
    {
        if (row.CollectedBank >= 0 && ByteKey.FromModBank(row.CollectedBank, row.CollectedAddress) is { } collected)
        {
            yield return collected;
        }

        if (row.RequiredBank >= 0 && ByteKey.FromModBank(row.RequiredBank, row.RequiredAddress) is { } required)
        {
            yield return required;
        }
    }

    private JsonArray ObjectRowsJson() =>
        new(context.Objects[a.FieldId].Select(row => (JsonNode)new JsonObject
        {
            ["entity"] = row.EntityId,
            ["kind"] = row.Kind.ToString(),
            ["nativeId"] = row.NativeId,
            ["label"] = row.Label,
            ["target"] = row.TargetKind.ToString(),
            ["flags"] = Array(ObjectKeys(row).Select(key => key.ToString()))
        }).ToArray());

    private JsonArray DialogsJson()
    {
        var array = new JsonArray();
        for (var id = 0; id < a.Section.DialogCount; id++)
        {
            try
            {
                var lines = context.Text?.ReadMessageLinesById(a.FieldId, id) ?? [];
                array.Add(new JsonObject { ["id"] = id, ["lines"] = Array(lines) });
            }
            catch (Exception exception)
            {
                array.Add(new JsonObject { ["id"] = id, ["error"] = exception.GetType().Name });
            }
        }

        return array;
    }

    private JsonArray InstructionsJson()
    {
        var array = new JsonArray();
        foreach (var instruction in a.Flow.Instructions.Values.OrderBy(instruction => instruction.Offset))
        {
            var item = new JsonObject
            {
                ["o"] = instruction.Offset,
                ["n"] = instruction.Name,
                ["h"] = instruction.Hex
            };
            if (a.Flow.JumpTarget(instruction.Offset) is { } target)
            {
                item["t"] = target;
            }

            if (!liveInstructions.Contains(instruction.Offset))
            {
                item["dormant"] = true;
            }

            if (a.Conditions.TryGetValue(instruction.Offset, out var condition))
            {
                item["test"] = condition.Text;
            }

            array.Add(item);
        }

        return array;
    }

    /// <summary>
    /// The field's part of the flag graph: savemap and temporary writes by the interaction
    /// that makes them, every test that reads a variable, and every effect a test on a
    /// variable guards.
    /// </summary>
    private void ContributeFlags(IReadOnlyList<EntryModel> roots)
    {
        foreach (var row in context.Story[a.FieldId])
        {
            foreach (var (key, mask) in StoryMasks(row))
            {
                var flag = global.Flag(key);
                flag.StoryFields.Add(a.FieldId);
                flag.TrackedMask |= mask;
            }
        }

        foreach (var row in context.Objects[a.FieldId])
        {
            foreach (var (key, mask) in ObjectMasks(row))
            {
                var flag = global.Flag(key);
                flag.ObjectFields.Add(a.FieldId);
                flag.TrackedMask |= mask;
            }
        }

        var debugRoom = GlobalAudit.IsDebugRoom(a.FieldName);
        if (a.File.ReadTriggers() is { } triggerTable)
        {
            foreach (var gateway in triggerTable.Gateways)
            {
                global.AddTransition(a.FieldId, gateway.Destination, "gateway");
            }
        }

        foreach (var root in roots)
        {
            foreach (var effect in closures[root.Id].Effects)
            {
                var trigger = Trigger(root, effect);
                if (effect.Effect.Kind == "MapJump")
                {
                    global.AddTransition(a.FieldId, BitConverter.ToUInt16(a.Flow.Instructions[effect.Effect.At].Bytes, 1), trigger);
                }

                if (debugRoom)
                {
                    continue;
                }

                if (effect.Effect.Kind is "PartySet" or "PartyAdd" or "PartyRemove")
                {
                    // The engine keeps the three party slots at Bank[3][9], [10] and [11].
                    var partyRecord = new JsonObject
                    {
                        ["field"] = a.FieldId,
                        ["fieldName"] = a.FieldName,
                        ["root"] = root.Id,
                        ["entityName"] = a.Entities[root.Entity].Name,
                        ["at"] = effect.Effect.At,
                        ["write"] = $"{effect.Effect.Kind} {effect.Effect.Detail}",
                        ["trigger"] = trigger,
                        ["must"] = Guards(effect.Must)
                    };
                    foreach (var address in new[] { 9, 10, 11 })
                    {
                        var flag = global.Flag(new ByteKey("2", address));
                        flag.AddWriter(a.FieldId, partyRecord, trigger);
                        flag.WrittenMask |= 0xFF;
                    }
                }

                if (effect.Effect.Kind is "SavemapWrite" or "TempWrite")
                {
                    foreach (var write in Semantics.Writes(a.Flow.Instructions[effect.Effect.At]))
                    {
                        if (write.Variable is null)
                        {
                            continue;
                        }

                        var record = new JsonObject
                        {
                            ["field"] = a.FieldId,
                            ["fieldName"] = a.FieldName,
                            ["root"] = root.Id,
                            ["entityName"] = a.Entities[root.Entity].Name,
                            ["at"] = effect.Effect.At,
                            ["write"] = effect.Effect.Detail,
                            ["trigger"] = trigger,
                            ["must"] = Guards(effect.Must)
                        };
                        var writeMask = write.Bit >= 0 ? 1 << write.Bit : 0xFF;
                        foreach (var key in ByteKey.Of(write.Variable))
                        {
                            var flag = global.Flag(key);
                            flag.AddWriter(a.FieldId, record, trigger);
                            flag.WrittenMask |= writeMask;
                        }
                    }
                }

                var gatingKinds = PerceivableKinds.Contains(effect.Effect.Kind) || AccessKinds.Contains(effect.Effect.Kind);
                if (!gatingKinds)
                {
                    continue;
                }

                foreach (var literal in effect.Must)
                {
                    if (!a.Conditions.TryGetValue(literal.At, out var condition))
                    {
                        continue;
                    }

                    foreach (var variable in new[] { condition.Left?.Variable, condition.Right?.Variable })
                    {
                        if (variable is null)
                        {
                            continue;
                        }

                        var record = new JsonObject
                        {
                            ["field"] = a.FieldId,
                            ["fieldName"] = a.FieldName,
                            ["root"] = root.Id,
                            ["entityName"] = a.Entities[root.Entity].Name,
                            ["trigger"] = trigger,
                            ["effect"] = $"{effect.Effect.At}:{effect.Effect.Kind} {effect.Effect.Detail}",
                            ["test"] = a.Describe(literal)
                        };
                        var gateMask = TestMask(condition, variable);
                        foreach (var key in ByteKey.Of(variable))
                        {
                            var flag = global.Flag(key);
                            flag.AddGate(a.FieldId, record, effect.Effect.Kind);
                            flag.GatedMask |= variable.Width == 1 ? gateMask : 0xFF;
                        }
                    }
                }
            }
        }

        foreach (var (at, condition) in a.Conditions)
        {
            if (!liveInstructions.Contains(at) || debugRoom)
            {
                continue;
            }

            foreach (var variable in new[] { condition.Left?.Variable, condition.Right?.Variable })
            {
                if (variable is null)
                {
                    continue;
                }

                global.Count(variable.Width == 2 && !(variable.Block == "1" && variable.Address == 0) ? "test.wordVariable" : "test.byteVariable");
                var record = new JsonObject
                {
                    ["field"] = a.FieldId,
                    ["fieldName"] = a.FieldName,
                    ["at"] = at,
                    ["test"] = condition.Text,
                    ["entries"] = Array(a.Entries.Where(entry => entry.Live && entry.Reach.Contains(at)).Select(entry => entry.Id).Take(6))
                };
                foreach (var key in ByteKey.Of(variable))
                {
                    global.Flag(key).AddReader(a.FieldId, record);
                }
            }
        }
    }

    private void CountField(IReadOnlyList<EntryModel> roots)
    {
        global.Count("field.analyzed");
        foreach (var instruction in a.Flow.Instructions.Values.Where(instruction => liveInstructions.Contains(instruction.Offset)))
        {
            if (instruction.Op == Opcodes.JMPFL)
            {
                var makou = instruction.Offset + 1 + BitConverter.ToUInt16(instruction.Bytes, 1);
                var shippingTarget = makou + 1;
                global.Count("jmpfl.live");
                global.Count(a.Flow.Instructions.ContainsKey(makou) ? "jmpfl.makouTargetIsInstruction" : "jmpfl.makouTargetNotInstruction");
                global.Count(a.Flow.Instructions.ContainsKey(shippingTarget) ? "jmpfl.shippingTargetIsInstruction" : "jmpfl.shippingTargetNotInstruction");
            }

            if (a.Conditions.TryGetValue(instruction.Offset, out var condition) && condition.Kind == ConditionKind.Compare)
            {
                global.Count(condition.Operator <= 10 ? $"test.operator.{Condition.OperatorNames[condition.Operator]}" : "test.operator.undefined");
            }

            if (instruction.Op is Opcodes.BITON or Opcodes.BITOFF or Opcodes.BITXOR)
            {
                var operands = Semantics.Operands(instruction);
                var position = operands.FirstOrDefault(operand => operand.Name == "position");
                global.Count(position is null || !position.IsImmediate ? "bit.positionFromVariable" : position.Raw >= 8 ? "bit.positionAbove7" : "bit.position0to7");
            }

            foreach (var operand in Semantics.Operands(instruction))
            {
                if (operand.Variable is { } variable)
                {
                    global.Count($"var.bank.{operand.Bank}");
                    if (variable.Address > 255)
                    {
                        global.Count("var.addressAbove255");
                    }

                    if (variable.Width == 2 && variable.Address % 2 == 1)
                    {
                        global.Count("var.oddWordAddress");
                    }

                    if (Opcodes.BankBlock(operand.Bank) is null)
                    {
                        global.Count("var.unmappedBank");
                    }
                }
            }

            if (Semantics.HasDynamicAddress(instruction))
            {
                global.Count("var.dynamicAddress");
            }
        }

        var row = new JsonObject
        {
            ["id"] = a.FieldId,
            ["name"] = a.FieldName,
            ["entities"] = a.Entities.Count,
            ["models"] = a.Entities.Count(entity => entity.Kind == EntityKind.Model),
            ["party"] = a.Entities.Count(entity => entity.Kind == EntityKind.PartyCharacter),
            ["lines"] = a.Entities.Count(entity => entity.Kind == EntityKind.Line),
            ["liveEntries"] = a.Entries.Count(entry => entry.Live),
            ["instructions"] = a.Flow.Instructions.Count,
            ["liveInstructions"] = liveInstructions.Count,
            ["anomalies"] = a.Flow.Anomalies.Count,
            ["shippingNpcs"] = shipping?.Npcs.Count ?? 0,
            ["shippingScriptExits"] = shipping?.Exits.Count ?? 0,
            ["storyRows"] = context.Story[a.FieldId].Count(),
            ["objectRows"] = context.Objects[a.FieldId].Count(),
            ["roots"] = roots.Count
        };
        global.FieldRows.Add(row);
    }

    private void AddCandidate(string klass, int priority, string summary, JsonObject witness)
    {
        witness["repro"] ??= Repro;
        if (GlobalAudit.IsDebugRoom(a.FieldName))
        {
            witness["debugRoom"] = true;
            priority = 4;
        }

        global.Add(new Candidate(klass, priority, a.FieldId, a.FieldName, summary, witness));
    }

    private JsonArray Guards(IEnumerable<Literal> literals) =>
        new(literals.Select(literal => (JsonNode)JsonValue.Create(a.Describe(literal))!).ToArray());

    private string Bytes(int at, int length)
    {
        var data = a.Section.Data;
        if (at < 0 || at >= data.Length)
        {
            return string.Empty;
        }

        return Convert.ToHexString(data, at, Math.Min(length, data.Length - at));
    }

    private static JsonArray Array<T>(IEnumerable<T> values) =>
        new(values.Select(value => JsonValue.Create(value) as JsonNode).ToArray());
}
