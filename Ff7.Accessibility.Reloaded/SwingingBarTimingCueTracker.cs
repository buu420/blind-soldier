namespace Ff7.Accessibility.Reloaded;

public sealed class SwingingBarTimingCueTracker
{
    public const ushort SwingingBarFieldId = 223;
    public const int AttemptWaitingBank = 5;
    public const int AttemptWaitingIndex = 29;
    public const int FrameCounterBank = 5;
    public const int FrameCounterIndex = 34;
    public const byte SuccessWindowStart = 54;
    public const byte SuccessWindowEnd = 62;
    public const int TriggerStartX = -468;
    public const int TriggerStartY = 1618;
    public const int TriggerStartZ = 3257;
    public const int TriggerEndX = -403;
    public const int TriggerEndY = 1651;
    public const int TriggerEndZ = 3273;
    public const int TriggerTolerance = 48;

    private bool hasPreviousCounter;
    private byte previousCounter;
    private bool announcedForAttempt;

    public byte LastObservedCounter { get; private set; }

    public bool Observe(
        byte currentModule,
        ushort fieldId,
        int playerX,
        int playerY,
        int playerZ,
        bool isAttemptWaiting,
        bool isUserControlLocked,
        byte frameCounter)
    {
        LastObservedCounter = frameCounter;
        if (currentModule != FieldPositionReader.FieldModule ||
            fieldId != SwingingBarFieldId ||
            !isAttemptWaiting ||
            !isUserControlLocked ||
            DistanceToTriggerLine(playerX, playerY, playerZ) > TriggerTolerance)
        {
            Reset();
            return false;
        }

        if (!hasPreviousCounter)
        {
            hasPreviousCounter = true;
            previousCounter = frameCounter;
            announcedForAttempt = false;
            return false;
        }

        var advanced = frameCounter > previousCounter;
        if (frameCounter < previousCounter)
        {
            // The native field script resets this temporary counter for each
            // new attempt. Rearm silently and wait for confirmed advancement.
            announcedForAttempt = false;
        }

        previousCounter = frameCounter;
        if (!advanced ||
            announcedForAttempt ||
            frameCounter < SuccessWindowStart ||
            frameCounter > SuccessWindowEnd)
        {
            return false;
        }

        announcedForAttempt = true;
        return true;
    }

    private static double DistanceToTriggerLine(int x, int y, int z)
    {
        var deltaX = TriggerEndX - TriggerStartX;
        var deltaY = TriggerEndY - TriggerStartY;
        var deltaZ = TriggerEndZ - TriggerStartZ;
        var lengthSquared =
            deltaX * (double)deltaX +
            deltaY * (double)deltaY +
            deltaZ * (double)deltaZ;
        if (lengthSquared <= 0d)
        {
            return Math.Sqrt(
                Math.Pow(x - TriggerStartX, 2d) +
                Math.Pow(y - TriggerStartY, 2d) +
                Math.Pow(z - TriggerStartZ, 2d));
        }

        var amount = Math.Clamp(
            ((x - TriggerStartX) * deltaX +
             (y - TriggerStartY) * deltaY +
             (z - TriggerStartZ) * deltaZ) / lengthSquared,
            0d,
            1d);
        var closestX = TriggerStartX + amount * deltaX;
        var closestY = TriggerStartY + amount * deltaY;
        var closestZ = TriggerStartZ + amount * deltaZ;
        return Math.Sqrt(
            Math.Pow(x - closestX, 2d) +
            Math.Pow(y - closestY, 2d) +
            Math.Pow(z - closestZ, 2d));
    }

    public void Reset()
    {
        hasPreviousCounter = false;
        previousCounter = 0;
        announcedForAttempt = false;
        LastObservedCounter = 0;
    }
}
