using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Which object a target is, independently of the slot it happens to occupy.
///
/// The slot's own word at +0xDA is not an identity: FUN_005EAF45 writes 1 into it for
/// every object it allocates and FUN_005EB2DF writes 0 on release, so it is a boolean
/// and a reused slot carries exactly the same 1 the previous occupant did. What does
/// differ is the object's node - FUN_005EF31E takes a fresh slot out of the node array
/// at 0x00C5D590 and stamps that slot into the node at +0x2A - together with the model
/// its type selects (node+0, from the table at 0x00C5D0F0), the type itself (node+0x28
/// and the object's own copy at +0x30) and its place in the node tree (node+0x24).
/// </summary>
public readonly record struct SpeedSquareCoasterTargetIdentity(
    uint Node,
    int NodeSlot,
    int NodeType,
    uint Model,
    int ObjectType,
    uint Parent);

/// <summary>
/// One target the last presented frame actually drew, in the same sight coordinates
/// the player's own cursor uses.
/// </summary>
/// <param name="Index">The native object slot.</param>
/// <param name="Identity">Which object this is, so a reused slot is not the same target.</param>
/// <param name="IsUnderSight">
/// The reticle currently overlaps this target's drawn box. This describes where the
/// sight is now; it is not a promise about a shot that has not been fired.
/// </param>
/// <param name="HitFlag">
/// The object's own flag at +0x2C, exactly as read. FUN_005E99FB only touches it
/// inside the half-space gate, so on its own it may be an older frame's answer.
/// </param>
/// <param name="HalfSpaceAccepted">
/// The gate FUN_005EECB5 applies to this object's composed world translation. It is
/// the condition under which the flag above is refreshed at all - not the renderer's
/// visibility test, which is a separate thing entirely.
/// </param>
public readonly record struct SpeedSquareCoasterTarget(
    int Index,
    SpeedSquareCoasterTargetIdentity Identity,
    int CentreX,
    int CentreY,
    int Width,
    int Height,
    bool IsUnderSight,
    bool HitFlag,
    bool HalfSpaceAccepted)
{
    /// <summary>
    /// A hit this frame resolved, rather than a flag left over from an older one.
    ///
    /// FUN_005E99FB clears +0x2C and then sets it only when the half-space accepts,
    /// the sight is strictly inside the drawn box, and the fire flag at 0x00C3FA70 is
    /// one. Seeing the flag set while all three of those conditions still hold in the
    /// same captured frame is what makes it current: the frame that wrote it is the
    /// frame being looked at. Seeing the flag set once the sight has moved off proves
    /// nothing, because the gate that would have cleared it may not have run.
    /// </summary>
    public bool IsResolvedHit(bool firing) =>
        HitFlag && HalfSpaceAccepted && IsUnderSight && firing;
}

/// <summary>
/// One whole, self-consistent observation. The cursor, the score and the fire flag
/// belong to it rather than being read separately, so a held frame is never described
/// with a later frame's sight position, score or trigger.
/// </summary>
/// <param name="CursorX">
/// The sight the game itself was showing, read from 0x00C3FB58 inside the capture -
/// not whatever the caller sampled before calling.
/// </param>
/// <param name="WasPresented">
/// The snapshot came from a frame the engine actually presented, rather than one of
/// the catch-up refreshes that update projected bounds without drawing anything.
/// </param>
public readonly record struct SpeedSquareCoasterTargetSnapshot(
    IReadOnlyList<SpeedSquareCoasterTarget> Targets,
    bool WasPresented,
    int CursorX,
    int CursorY,
    int Score,
    bool Firing);

/// <summary>
/// Reads the shooting coaster's currently drawn targets and the displayed score.
///
/// The whole point of this class is that a projected box is not evidence of a visible
/// model. Everything below is the installed executable's own answer to "is this thing
/// on screen", reproduced rather than approximated.
///
/// <para>The per-object frame, FUN_005E99FB.</para>
/// It takes the object's node. When the node's parent (node+0x24) is the root at
/// 0x00C60150 it composes the node's own matrix at node+4 with the shared camera
/// rotation at 0x00C3F8A0; otherwise it composes the parent's matrix with the node's
/// first, and that result with the camera. It zeroes the camera matrix's translation
/// beforehand. It then renders the model's triangles - but only while 0x009014A8 is
/// non-zero - and afterwards projects the geometry at +0xDC into the packed bounds at
/// +0x11C regardless of whether anything was drawn.
///
/// <para>The matrices.</para>
/// The PC MATRIX is packed and 0x1E bytes: nine signed shorts, then three signed int
/// translations at +0x12, +0x16 and +0x1A with no padding in between. FUN_006611FB
/// converts one to a float 4x4 by dividing the rotation by 4096 and keeping the
/// translation as it is, laid out row-major with the translation in the fourth row.
/// FUN_0066C984 multiplies two of those as C = A * B, which is the row-vector
/// convention, and FUN_005F2759 confirms it: the view depth of a point is
/// x*m[2] + y*m[6] + z*m[10] + m[14], the third column. FUN_005F27BF then multiplies
/// by the global view matrix at game+0x2FC, which FUN_005E8AEC sets to the identity
/// for this minigame, so it changes nothing here.
///
/// <para>What counts as drawn, FUN_005EFA3A and FUN_005F04D7.</para>
/// The node's model pointer is node+0; its triangle count is the short at model+4 and
/// its triangle array is the pointer at model+0xC, walked at a stride of 0x24. Each
/// triangle is three eight-byte vertices at +0, +8 and +0x10. The renderer transforms
/// all three, and draws the triangle only when at least one of them has a view depth
/// greater than zero; below -100 it takes the clipping path instead. Per-vertex light
/// comes from FUN_005F2838 against 0x00C3F750 and 0x00C3F754, which returns the dark
/// constant at or beyond the far bound.
///
/// So a model is visible when it has at least one triangle with a vertex in front of
/// the camera and a vertex that is not drawn fully black. Neither the node's origin
/// nor the size of its projected box can stand in for that: an origin in front of the
/// camera can have every vertex behind it, and an origin behind can have perfectly
/// visible geometry in front.
///
/// <para>What the half-space gate is, FUN_005EECB5.</para>
/// It is not the visibility test. FUN_005E99FB applies it to the composed world
/// translation *after* rendering, and uses it to guard the hit flag at +0x2C: inside
/// the gate the flag is cleared and then set when the sight is strictly inside the
/// folded box and 0x00C3FA70 says the player is firing. Using it to decide what is on
/// screen would silence targets the player can see and shoot.
///
/// <para>Presentation.</para>
/// FUN_005E8E7E runs catch-up refreshes with 0x009014A8 clear and one presented
/// refresh with it set, and the projected bounds are rewritten either way. A snapshot
/// taken during a catch-up pass describes an image nobody saw, so the last presented
/// snapshot is held whole - cursor, score and fire flag included - rather than mixed
/// with the current ones.
///
/// <para>Capture.</para>
/// None of this is read atomically, and the things it rests on move constantly while
/// the module and the presentation flag stay exactly where they are: the sight moves
/// under the player's hand, the camera moves under the ride, the objects move along
/// their own paths, slots are released and handed straight out again, and the trigger
/// goes up and down. Every one of those is sampled either side of the read and the
/// frame is refused if any of them moved, because a snapshot that straddled one is a
/// picture of nothing. A refused frame is simply read again on the next ordinary
/// poll; nothing here retries in place.
///
/// <para>What is deliberately not reproduced.</para>
/// FUN_005F2639's perspective divisor. It divides by a component of a transform
/// through the matrix at renderer+0x84, and that renderer at 0x00C3F888 is one shared
/// scratch context overwritten for every object in the loop, so after a frame it
/// holds whichever object happened to be last. The projected bounds the object itself
/// owns are used instead.
/// </summary>
public sealed class SpeedSquareCoasterTargetReader
{
    public const int AddressObjectArray = 0x00C3FB68;
    public const int ObjectStride = 0x13C;
    public const int ObjectCount = 100;

    public const int ObjectHitFlagOffset = 0x2C;

    /// <summary>
    /// The object's copy of the spawn record's type index, which FUN_005EAF45 uses to
    /// pick the geometry template at 0x00C5D0E4 + type * 0x14.
    /// </summary>
    public const int ObjectTypeOffset = 0x30;

    /// <summary>
    /// In use, not a generation counter. FUN_005EAF45 writes 1 and FUN_005EB2DF
    /// writes 0; no path ever writes anything else.
    /// </summary>
    public const int ObjectActiveOffset = 0xDA;

    public const int ObjectNodePointerOffset = 0xD4;
    public const int ObjectProjectedBoundsOffset = 0x11C;

    /// <summary>The node's model pointer, from the table at 0x00C5D0F0.</summary>
    public const int NodeModelPointerOffset = 0x00;

    /// <summary>The node's own packed PSX MATRIX.</summary>
    public const int NodeMatrixOffset = 0x04;

    /// <summary>
    /// The translation inside that matrix. The packed matrix has no padding after its
    /// nine shorts, so this is node + 4 + 0x12.
    /// </summary>
    public const int NodeTranslationOffset = 0x16;

    public const int NodeParentOffset = 0x24;
    public const int NodeTypeOffset = 0x28;
    public const int NodeSlotOffset = 0x2A;

    /// <summary>The node every top-level coaster object hangs from.</summary>
    public const uint AddressRootParentNode = 0x00C60150;

    public const int PackedMatrixBytes = 0x1E;
    public const int PackedMatrixTranslationOffset = 0x12;

    public const int ModelTriangleCountOffset = 0x04;
    public const int ModelTrianglePointerOffset = 0x0C;
    public const int TriangleStride = 0x24;
    public const int TriangleVertexCount = 3;
    public const int TriangleVertexStride = 8;

    /// <summary>
    /// A sanity bound on the triangle walk. Coaster props are small; a count larger
    /// than this is a torn read of the model header rather than a model.
    /// </summary>
    public const int MaximumTrianglesPerModel = 1024;

    /// <summary>FUN_005E99FB's own fold bound: only the initialised points.</summary>
    public const int UsableBoundPoints = 6;

    public const int AddressPresentationFlag = 0x009014A8;
    public const int AddressCameraRotation = 0x00C3F8A0;
    public const int AddressLightNear = 0x00C3F750;
    public const int AddressLightFar = 0x00C3F754;

    /// <summary>
    /// The half-space plane data, read as one block because every piece of it belongs
    /// to the same gate: two constants, two coefficient triples and two references.
    /// </summary>
    public const int AddressHalfSpaceBlock = 0x00C5D328;

    public const int HalfSpaceBlockBytes = 0x30;
    public const int AddressHalfSpaceConstantA = 0x00C5D328;
    public const int AddressHalfSpaceConstantB = 0x00C5D32C;
    public const int AddressHalfSpaceCoefficientsA = 0x00C5D330;
    public const int AddressHalfSpaceCoefficientsB = 0x00C5D340;
    public const int AddressHalfSpaceReferenceA = 0x00C5D350;
    public const int AddressHalfSpaceReferenceB = 0x00C5D354;

    /// <summary>
    /// The sight's own position, which FUN_005E99FB scales and offsets into screen
    /// space to test against each folded box. This is the sight the game is drawing,
    /// and it is what a snapshot reports; a value the caller sampled before calling is
    /// already older than the frame being captured.
    /// </summary>
    public const int AddressSightCursorX = 0x00C3FB58;

    public const int AddressSightCursorY = 0x00C3FB5C;

    public const int AddressSightScaleX = 0x0090147C;
    public const int AddressSightScaleY = 0x00901480;
    public const int AddressSightOffsetX = 0x00C3F784;
    public const int AddressSightOffsetY = 0x00C3F788;
    public const int AddressFiring = 0x00C3FA70;
    public const int AddressScore = 0x00C3F74C;

    /// <summary>PSX fixed point: 4096 is 1.0. FUN_006611FB divides by exactly this.</summary>
    public const double RotationScale = 4096d;

    /// <summary>FUN_005F04D7's own two constants: 0.0 at 0x007B7810, -100.0 at 0x007B7818.</summary>
    public const double InFrontDepth = 0d;

    public const double ClipDepth = -100d;

    // The captured frame, laid out in one buffer so it can be compared in one go.
    private const int CaptureModule = 0;
    private const int CaptureFiring = 1;
    private const int CapturePresentation = 4;
    private const int CaptureSightCursor = 8;
    private const int CaptureSightScale = 16;
    private const int CaptureSightOffset = 24;
    private const int CaptureScore = 32;
    private const int CaptureLightNear = 36;
    private const int CaptureLightFar = 40;
    private const int CaptureCamera = 44;
    private const int CaptureHalfSpace = CaptureCamera + PackedMatrixBytes;
    private const int CaptureBytes = CaptureHalfSpace + HalfSpaceBlockBytes;

    private readonly ILegacyAddressSpace memory;
    private SpeedSquareCoasterTargetSnapshot lastPresented;
    private bool hasPresented;

    public SpeedSquareCoasterTargetReader(ILegacyAddressSpace memory)
    {
        this.memory = memory ?? throw new ArgumentNullException(nameof(memory));
    }

    public string LastDiagnostic { get; private set; } = string.Empty;

    public void Reset()
    {
        lastPresented = default;
        hasPresented = false;
        LastDiagnostic = string.Empty;
    }

    public bool TryReadScore(out int score) =>
        memory.TryReadInt32((uint)AddressScore, out score) && score is >= 0 and <= 9999;

    public bool TryReadFiring(out bool firing)
    {
        firing = false;
        if (!memory.TryReadByte((uint)AddressFiring, out var value))
        {
            return false;
        }

        firing = value == 1;
        return true;
    }

    /// <summary>
    /// The targets the last presented frame drew, nearest to the sight first, with the
    /// cursor, score and fire flag that belong to the same capture.
    ///
    /// <paramref name="callerCursorX"/> and <paramref name="callerCursorY"/> are what
    /// the caller last saw. They are not used to describe anything: the sight the
    /// snapshot reports is the one read inside the capture, because the caller's copy
    /// was already taken before this frame began. They are kept only so the caller can
    /// be told, through the diagnostic, that its own reading had gone stale.
    ///
    /// An unreadable or torn frame returns false rather than an empty list, so a
    /// translation miss, a module change or a frame that moved under the read is
    /// silence rather than "everything has gone". The next ordinary poll reads again.
    /// </summary>
    public bool TryReadTargets(
        int callerCursorX,
        int callerCursorY,
        out SpeedSquareCoasterTargetSnapshot snapshot)
    {
        snapshot = default;
        Span<byte> capture = stackalloc byte[CaptureBytes];
        if (!TryCaptureFrame(capture))
        {
            return false;
        }

        var module = capture[CaptureModule];
        if (module != SpeedSquareCoasterStateReader.CoasterModule)
        {
            LastDiagnostic = $"module {module} is not the shooting coaster";
            return false;
        }

        if (ReadInt32(capture, CapturePresentation) == 0)
        {
            // A catch-up refresh rewrote the bounds without drawing anything. The
            // whole previous observation is held, not just its target list: pairing
            // last frame's boxes with this frame's sight would describe a position
            // that never existed.
            snapshot = lastPresented with { WasPresented = false };
            LastDiagnostic =
                $"catch-up refresh; holding {lastPresented.Targets?.Count ?? 0} presented targets";
            return hasPresented;
        }

        var cursorX = ReadInt32(capture, CaptureSightCursor);
        var cursorY = ReadInt32(capture, CaptureSightCursor + 4);
        var scaleX = ReadInt32(capture, CaptureSightScale);
        var scaleY = ReadInt32(capture, CaptureSightScale + 4);
        var offsetX = ReadInt32(capture, CaptureSightOffset);
        var offsetY = ReadInt32(capture, CaptureSightOffset + 4);
        var score = ReadInt32(capture, CaptureScore);
        var firing = capture[CaptureFiring] == 1;
        var lightFar = ReadInt32(capture, CaptureLightFar);
        var lightNear = ReadInt32(capture, CaptureLightNear);

        if (scaleX == 0 || scaleY == 0)
        {
            LastDiagnostic = "sight transform is not yet initialised";
            return false;
        }

        if (lightFar <= lightNear)
        {
            LastDiagnostic = $"light bounds are not usable: near={lightNear}, far={lightFar}";
            return false;
        }

        if (score is < 0 or > 9999)
        {
            LastDiagnostic = $"displayed score {score} is out of range";
            return false;
        }

        var sightScreenX = (cursorX * scaleX) + offsetX;
        var sightScreenY = (cursorY * scaleY) + offsetY;
        var camera = ParsePackedMatrix(capture.Slice(CaptureCamera, PackedMatrixBytes)).WithoutTranslation();
        var halfSpace = ParseHalfSpace(capture.Slice(CaptureHalfSpace, HalfSpaceBlockBytes));

        var found = new List<SpeedSquareCoasterTarget>(8);
        Span<byte> bounds = stackalloc byte[UsableBoundPoints * sizeof(int)];
        Span<byte> nodeMatrix = stackalloc byte[PackedMatrixBytes];
        Span<byte> parentMatrix = stackalloc byte[PackedMatrixBytes];
        Span<byte> recheck = stackalloc byte[PackedMatrixBytes];
        for (var index = 0; index < ObjectCount; index++)
        {
            var objectBase = (uint)(AddressObjectArray + (index * ObjectStride));
            if (!memory.TryReadInt16(objectBase + ObjectActiveOffset, out var active))
            {
                LastDiagnostic = $"object {index} active word unreadable";
                return false;
            }

            // In use or not. Nothing else is encoded here.
            if (active == 0)
            {
                continue;
            }

            if (!memory.TryReadUInt32(objectBase + ObjectNodePointerOffset, out var nodeAddress) ||
                !IsPlausibleGuestPointer(nodeAddress))
            {
                LastDiagnostic = $"object {index} node pointer unreadable";
                return false;
            }

            if (!memory.TryReadUInt32(nodeAddress + NodeModelPointerOffset, out var model) ||
                !memory.TryReadUInt32(nodeAddress + NodeParentOffset, out var parent) ||
                !memory.TryReadInt16(nodeAddress + NodeTypeOffset, out var nodeType) ||
                !memory.TryReadInt16(nodeAddress + NodeSlotOffset, out var nodeSlot) ||
                !memory.TryRead(nodeAddress + NodeMatrixOffset, nodeMatrix))
            {
                LastDiagnostic = $"object {index} node unreadable";
                return false;
            }

            var hasParentMatrix = parent != AddressRootParentNode;
            if (hasParentMatrix &&
                (!IsPlausibleGuestPointer(parent) ||
                 !memory.TryRead(parent + NodeMatrixOffset, parentMatrix)))
            {
                LastDiagnostic = $"object {index} parent matrix unreadable";
                return false;
            }

            // FUN_005E99FB's own composition order: a root-parented node goes straight
            // to the camera, anything else has its parent applied first.
            var node = ParsePackedMatrix(nodeMatrix);
            var world = hasParentMatrix
                ? Matrix4x4.Multiply(Matrix4x4.Multiply(ParsePackedMatrix(parentMatrix), node), camera)
                : Matrix4x4.Multiply(node, camera);

            if (!TryReadModelVisibility(model, in world, lightFar, out var isDrawn))
            {
                LastDiagnostic = $"object {index} model geometry unreadable";
                return false;
            }

            // The renderer's own answer: no triangle of this model has a vertex in
            // front of the camera that is drawn in anything but black.
            if (!isDrawn)
            {
                continue;
            }

            if (!memory.TryRead(objectBase + ObjectProjectedBoundsOffset, bounds) ||
                !memory.TryReadInt32(objectBase + ObjectHitFlagOffset, out var hitFlag) ||
                !memory.TryReadInt32(objectBase + ObjectTypeOffset, out var objectType))
            {
                LastDiagnostic = $"object {index} projected bounds unreadable";
                return false;
            }

            // The object's own bookend. Its slot can be released and handed straight
            // out again, its node can be replaced while the slot stays in use, and it
            // moves along its own path every frame - none of which touches the module
            // or the presentation flag. Boxes projected from one transform must not be
            // described against another.
            if (!ObjectStillHolds(
                    objectBase, nodeAddress, active, nodeMatrix, hasParentMatrix, parent, parentMatrix,
                    recheck))
            {
                LastDiagnostic = $"object {index} changed while it was being read";
                return false;
            }

            var minX = int.MaxValue;
            var maxX = int.MinValue;
            var minY = int.MaxValue;
            var maxY = int.MinValue;
            for (var point = 0; point < UsableBoundPoints; point++)
            {
                var packed = BinaryPrimitives.ReadUInt32LittleEndian(
                    bounds.Slice(point * sizeof(int), sizeof(int)));
                int x = unchecked((short)(packed & 0xFFFF));
                int y = unchecked((short)((packed >> 16) & 0xFFFF));
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }

            // Back into sight coordinates, which is the space the player's own cursor
            // and the spoken readout both use.
            var leftSight = DivideToSight(minX - offsetX, scaleX);
            var rightSight = DivideToSight(maxX - offsetX, scaleX);
            var topSight = DivideToSight(minY - offsetY, scaleY);
            var bottomSight = DivideToSight(maxY - offsetY, scaleY);
            var width = rightSight - leftSight;
            var height = bottomSight - topSight;

            // Degenerate, or entirely outside the viewport the sight moves in.
            if (width <= 0 || height <= 0 ||
                rightSight < 0 || leftSight > SpeedSquareCoasterState.ScreenWidth ||
                bottomSight < 0 || topSight > SpeedSquareCoasterState.ScreenHeight)
            {
                continue;
            }

            // Both in the native's own screen space, exactly as FUN_005E99FB writes
            // them: strictly inside on all four sides, and the gate that guards the
            // hit flag applied to the composed world translation.
            var underSight = maxX > sightScreenX && minX < sightScreenX &&
                             maxY > sightScreenY && minY < sightScreenY;
            var accepted = halfSpace.Accepts(
                (int)Math.Round(world.TranslationX),
                (int)Math.Round(world.TranslationY),
                (int)Math.Round(world.TranslationZ));

            found.Add(new SpeedSquareCoasterTarget(
                index,
                new SpeedSquareCoasterTargetIdentity(
                    nodeAddress, nodeSlot, nodeType, model, objectType, parent),
                (leftSight + rightSight) / 2,
                (topSight + bottomSight) / 2,
                width,
                height,
                underSight,
                hitFlag == 1,
                accepted));
        }

        // The frame's own bookend, over every shared input the objects above were read
        // against.
        Span<byte> after = stackalloc byte[CaptureBytes];
        if (!TryCaptureFrame(after))
        {
            return false;
        }

        if (!capture.SequenceEqual(after))
        {
            LastDiagnostic = $"coaster frame changed while it was being read: {Describe(capture, after)}";
            return false;
        }

        found.Sort((left, right) =>
            SquaredSightDistance(left, cursorX, cursorY)
                .CompareTo(SquaredSightDistance(right, cursorX, cursorY)));
        snapshot = new SpeedSquareCoasterTargetSnapshot(
            found, WasPresented: true, cursorX, cursorY, score, firing);
        lastPresented = snapshot;
        hasPresented = true;
        LastDiagnostic =
            $"coaster targets={found.Count}, sight=({cursorX},{cursorY}), score={score}, " +
            $"firing={firing}, presented" +
            (callerCursorX == cursorX && callerCursorY == cursorY
                ? string.Empty
                : $"; caller sight ({callerCursorX},{callerCursorY}) was already stale");
        return true;
    }

    /// <summary>
    /// Everything a whole observation rests on, in one buffer so that comparing two
    /// captures is one operation. Every field here can move while the module and the
    /// presentation flag stay exactly where they are.
    /// </summary>
    private bool TryCaptureFrame(Span<byte> capture)
    {
        capture.Clear();
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module))
        {
            LastDiagnostic = "module selector unreadable";
            return false;
        }

        capture[CaptureModule] = module;
        if (!memory.TryReadByte((uint)AddressFiring, out var firing))
        {
            LastDiagnostic = "fire flag unreadable";
            return false;
        }

        capture[CaptureFiring] = firing;
        if (!memory.TryRead((uint)AddressPresentationFlag, capture.Slice(CapturePresentation, 4)) ||
            !memory.TryRead((uint)AddressSightCursorX, capture.Slice(CaptureSightCursor, 8)) ||
            !memory.TryRead((uint)AddressSightScaleX, capture.Slice(CaptureSightScale, 8)) ||
            !memory.TryRead((uint)AddressSightOffsetX, capture.Slice(CaptureSightOffset, 8)) ||
            !memory.TryRead((uint)AddressScore, capture.Slice(CaptureScore, 4)) ||
            !memory.TryRead((uint)AddressLightNear, capture.Slice(CaptureLightNear, 4)) ||
            !memory.TryRead((uint)AddressLightFar, capture.Slice(CaptureLightFar, 4)) ||
            !memory.TryRead((uint)AddressCameraRotation, capture.Slice(CaptureCamera, PackedMatrixBytes)) ||
            !memory.TryRead((uint)AddressHalfSpaceBlock, capture.Slice(CaptureHalfSpace, HalfSpaceBlockBytes)))
        {
            LastDiagnostic = "coaster frame state unreadable";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the object that was read is still the same object, in the same place.
    /// The slot's in-use word cannot answer that on its own - a released slot is
    /// handed out again carrying the same 1 - so the node it owns and the transform
    /// that node holds are checked as well.
    /// </summary>
    private bool ObjectStillHolds(
        uint objectBase,
        uint nodeAddress,
        short active,
        ReadOnlySpan<byte> nodeMatrix,
        bool hasParentMatrix,
        uint parent,
        ReadOnlySpan<byte> parentMatrix,
        Span<byte> scratch)
    {
        if (!memory.TryReadInt16(objectBase + ObjectActiveOffset, out var activeAfter) ||
            activeAfter != active)
        {
            return false;
        }

        if (!memory.TryReadUInt32(objectBase + ObjectNodePointerOffset, out var nodeAfter) ||
            nodeAfter != nodeAddress)
        {
            return false;
        }

        if (!memory.TryRead(nodeAddress + NodeMatrixOffset, scratch) ||
            !scratch.SequenceEqual(nodeMatrix))
        {
            return false;
        }

        if (!hasParentMatrix)
        {
            return true;
        }

        return memory.TryRead(parent + NodeMatrixOffset, scratch) &&
               scratch.SequenceEqual(parentMatrix);
    }

    /// <summary>
    /// Walks the model's triangles the way FUN_005EFA3A does and applies FUN_005F04D7's
    /// own test to each: a triangle is drawn when at least one of its three transformed
    /// vertices is in front of the camera, and it is worth describing when at least one
    /// of them is not drawn in the dark constant FUN_005F2838 returns at and beyond the
    /// far bound. The vertex buffer is taken once for the whole walk.
    /// </summary>
    private bool TryReadModelVisibility(uint model, in Matrix4x4 world, int lightFar, out bool isDrawn)
    {
        isDrawn = false;
        if (!IsPlausibleGuestPointer(model))
        {
            return false;
        }

        if (!memory.TryReadInt16(model + ModelTriangleCountOffset, out var triangleCount) ||
            !memory.TryReadUInt32(model + ModelTrianglePointerOffset, out var triangles))
        {
            return false;
        }

        if (triangleCount <= 0)
        {
            // A model with no geometry draws nothing. That is an answer, not a failure
            // to read one.
            return true;
        }

        if (triangleCount > MaximumTrianglesPerModel || !IsPlausibleGuestPointer(triangles))
        {
            return false;
        }

        Span<byte> vertices = stackalloc byte[TriangleVertexCount * TriangleVertexStride];
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            if (!memory.TryRead(triangles + (uint)(triangle * TriangleStride), vertices))
            {
                return false;
            }

            var anyInFront = false;
            var anyLit = false;
            for (var vertex = 0; vertex < TriangleVertexCount; vertex++)
            {
                var at = vertex * TriangleVertexStride;
                var x = BinaryPrimitives.ReadInt16LittleEndian(vertices[at..]);
                var y = BinaryPrimitives.ReadInt16LittleEndian(vertices[(at + 2)..]);
                var z = BinaryPrimitives.ReadInt16LittleEndian(vertices[(at + 4)..]);
                var depth = world.Depth(x, y, z);
                anyInFront |= depth > InFrontDepth;
                anyLit |= depth < lightFar;
            }

            if (anyInFront && anyLit)
            {
                isDrawn = true;
                return true;
            }
        }

        return true;
    }

    /// <summary>
    /// FUN_006611FB: a packed 0x1E-byte PSX MATRIX read as the row-major float 4x4 it
    /// is converted to, rotation over 4096 and translation kept as it is.
    /// </summary>
    private static Matrix4x4 ParsePackedMatrix(ReadOnlySpan<byte> packed)
    {
        var matrix = default(Matrix4x4);
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var element = (row * 3) + column;
                matrix[(row * 4) + column] =
                    BinaryPrimitives.ReadInt16LittleEndian(packed[(element * sizeof(short))..]) /
                    RotationScale;
            }
        }

        for (var axis = 0; axis < 3; axis++)
        {
            matrix[12 + axis] = BinaryPrimitives.ReadInt32LittleEndian(
                packed[(PackedMatrixTranslationOffset + (axis * sizeof(int)))..]);
        }

        matrix[15] = 1d;
        return matrix;
    }

    private static HalfSpaceGate ParseHalfSpace(ReadOnlySpan<byte> block) =>
        new(ReadInt32(block, 0),
            ReadInt32(block, 8),
            ReadInt32(block, 12),
            ReadInt32(block, 16),
            ReadInt32(block, 4),
            ReadInt32(block, 24),
            ReadInt32(block, 28),
            ReadInt32(block, 32),
            ReadInt32(block, 40),
            ReadInt32(block, 44));

    private static string Describe(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after)
    {
        if (before[CaptureModule] != after[CaptureModule])
        {
            return $"module {before[CaptureModule]}->{after[CaptureModule]}";
        }

        if (before[CaptureFiring] != after[CaptureFiring])
        {
            return $"fire flag {before[CaptureFiring]}->{after[CaptureFiring]}";
        }

        if (!before.Slice(CapturePresentation, 4).SequenceEqual(after.Slice(CapturePresentation, 4)))
        {
            return "presentation state moved";
        }

        if (!before.Slice(CaptureSightCursor, 8).SequenceEqual(after.Slice(CaptureSightCursor, 8)))
        {
            return $"sight ({ReadInt32(before, CaptureSightCursor)},{ReadInt32(before, CaptureSightCursor + 4)})" +
                   $"->({ReadInt32(after, CaptureSightCursor)},{ReadInt32(after, CaptureSightCursor + 4)})";
        }

        if (!before.Slice(CaptureScore, 4).SequenceEqual(after.Slice(CaptureScore, 4)))
        {
            return $"score {ReadInt32(before, CaptureScore)}->{ReadInt32(after, CaptureScore)}";
        }

        if (!before.Slice(CaptureCamera, PackedMatrixBytes)
                .SequenceEqual(after.Slice(CaptureCamera, PackedMatrixBytes)))
        {
            return "camera moved";
        }

        if (!before.Slice(CaptureHalfSpace, HalfSpaceBlockBytes)
                .SequenceEqual(after.Slice(CaptureHalfSpace, HalfSpaceBlockBytes)))
        {
            return "hit gate planes moved";
        }

        return "sight transform or light bounds moved";
    }

    private static int ReadInt32(ReadOnlySpan<byte> buffer, int offset) =>
        BinaryPrimitives.ReadInt32LittleEndian(buffer[offset..]);

    private static bool IsPlausibleGuestPointer(uint pointer) =>
        FieldMovieNarrationSampleReader.IsPlausibleGuestPointer(pointer);

    private static int DivideToSight(int screenDelta, int scale) =>
        (int)Math.Round(screenDelta / (double)scale, MidpointRounding.AwayFromZero);

    private static long SquaredSightDistance(SpeedSquareCoasterTarget target, int cursorX, int cursorY)
    {
        long dx = target.CentreX - cursorX;
        long dy = target.CentreY - cursorY;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>Sixteen doubles held inline, so composing a matrix allocates nothing.</summary>
    [InlineArray(16)]
    private struct MatrixStorage
    {
        private double element;
    }

    /// <summary>
    /// A row-major 4x4 with the translation in the fourth row, which is the shape
    /// FUN_006611FB produces and FUN_0066C984 multiplies.
    /// </summary>
    private struct Matrix4x4
    {
        private MatrixStorage values;

        public double this[int index]
        {
            get => values[index];
            set => values[index] = value;
        }

        public double TranslationX => values[12];

        public double TranslationY => values[13];

        public double TranslationZ => values[14];

        /// <summary>FUN_0066C984: C = A * B, row by row.</summary>
        public static Matrix4x4 Multiply(in Matrix4x4 left, in Matrix4x4 right)
        {
            var result = default(Matrix4x4);
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    var sum = 0d;
                    for (var inner = 0; inner < 4; inner++)
                    {
                        sum += left[(row * 4) + inner] * right[(inner * 4) + column];
                    }

                    result[(row * 4) + column] = sum;
                }
            }

            return result;
        }

        /// <summary>
        /// FUN_005F2759, exactly: the third column, which under the row-vector
        /// convention this matrix is built for is the point's view depth.
        /// </summary>
        public readonly double Depth(int x, int y, int z) =>
            (x * values[2]) + (y * values[6]) + (z * values[10]) + values[14];

        public readonly Matrix4x4 WithoutTranslation()
        {
            var copy = this;
            copy[12] = 0d;
            copy[13] = 0d;
            copy[14] = 0d;
            return copy;
        }
    }

    /// <summary>
    /// FUN_005EECB5, exactly: two plane values from the composed world translation with
    /// each coordinate arithmetic-shifted right by two, each accepted only when it
    /// carries the same strictly non-zero sign as its reference. This guards the hit
    /// flag; it is not the renderer's visibility test.
    /// </summary>
    private readonly record struct HalfSpaceGate(
        int ConstantA,
        int Ax,
        int Ay,
        int Az,
        int ConstantB,
        int Bx,
        int By,
        int Bz,
        int ReferenceA,
        int ReferenceB)
    {
        public bool Accepts(int x, int y, int z)
        {
            var shiftedX = x >> 2;
            var shiftedY = y >> 2;
            var shiftedZ = z >> 2;
            var a = ((long)Ax * shiftedX) + ((long)Ay * shiftedY) + ConstantA + ((long)Az * shiftedZ);
            var b = ((long)Bx * shiftedX) + ((long)By * shiftedY) + ConstantB + ((long)Bz * shiftedZ);
            return SameSign(a, ReferenceA) && SameSign(b, ReferenceB);
        }

        private static bool SameSign(long value, int reference) =>
            (value > 0 && reference > 0) || (value < 0 && reference < 0);
    }
}
