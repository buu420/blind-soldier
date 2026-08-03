namespace Ff7.Accessibility.Reloaded;

public sealed class OpeningMovieDescription
{
    public const int MovieEndSeconds = 115;

    public static IReadOnlyList<OpeningMovieCue> Cues { get; } =
    [
        new(0, "Black screen. White pinpoint stars slowly appear, scattered across a dark void. They drift gently, as if floating in space."),
        new(7, "Stars continue to drift. A faint green luminescence begins to bleed in among them - soft, organic, like glowing mist. The green glow gradually brightens and intensifies."),
        new(30, "Cut to: extreme close-up of a young woman's face, lit from below by a soft green light. She has long honey-blonde hair pulled up with a large pink bow. Her eyes are a vivid, unnaturally bright green. She gazes downward, expression calm and inward."),
        new(45, "The camera pulls back slowly. She is crouching in a dark, narrow alley. Her hands are held close to her chest; tiny green particles of light float around her like embers. She wears a red bolero jacket over a pink dress."),
        new(51, "She stands and turns. A large wicker basket full of yellow and white flowers hangs from her arm. She is surrounded by the drifting green particles. Her gaze lifts slightly, as if sensing something."),
        new(56, "She glances sharply upward and to the right - a look of sudden alertness. The green particles swirl around her. She takes a small step back."),
        new(62, "Hard cut to black, then: a rapid low-angle shot looking straight up at massive rotating industrial gears and machinery, backlit by a blazing circular light source. The camera tilts and sweeps away violently."),
        new(64, "Cut to: an aerial view descending rapidly toward a dense, dark industrial city district at night. Neon signs and street lights dot the scene far below. The descent is swift - the city rushes upward to fill the frame."),
        new(68, "Street level. A busy urban street at night. A sign on the left reads \"SONS\" in large illuminated letters. Across the street, an orange neon sign reads \"GOBLINS BAR.\" Pedestrians move through the frame in silhouette. The street is crowded, grimy, and neon-lit."),
        new(73, "The camera pushes forward at street level through the crowd. A man in a dark coat and hat is prominent mid-frame. Other figures pass as dark silhouettes. The alley narrows ahead."),
        new(77, "Cut to: high overhead shot looking straight down at a large ornate building - arched windows, a circular central plaza, and a round fountain at its base. The camera cranes slowly downward toward the structure."),
        new(82, "Cut to: a massive cylindrical industrial machine, viewed from very close. Glowing blue circular intakes line its base. Red hazard-striped panels are visible. Steam vents from multiple ports. The camera slowly pushes in toward its face."),
        new(88, "The title card fades in over the machine: \"FINAL FANTASY VII\" in large silver letters with a stylized wing emblem above. Below, smaller text reads: \"(c) 1997 SQUARE.\" The music swells."),
        new(93, "Title fades. The camera pulls back to reveal the full scale of an enormous circular industrial complex seen from the air - dozens of smokestacks billow white smoke; glowing green lights pulse throughout the structure. The facility fills the entire frame."),
        new(100, "The camera descends and drifts along the outside of the complex - past massive cylindrical towers, pipes, catwalks, and vents. A red circular emblem is visible on one tower face. The structure extends far above and below the frame."),
        new(108, "The camera continues descending the exterior, moving past layers of scaffolding, ducts, and machinery. The bottom edge of the complex comes into view, and beneath it: a narrow ground-level street passage. Yellow-and-black hazard stripes line the walls."),
        new(115, "The camera reaches ground level and levels out into a straight forward-moving shot - a dark industrial street, flanked by pipes and machinery on both sides. A faint light glows at the far end of the passage. The cinematic holds on this view, then cuts to black. Gameplay begins.")
    ];

    private readonly Action<string, bool> speak;
    private readonly Func<DateTime> utcNow;
    private DateTime startedAt = DateTime.MinValue;
    private int nextCueIndex;

    public OpeningMovieDescription(Action<string> speak)
        : this((text, _) => speak(text), () => DateTime.UtcNow)
    {
    }

    public OpeningMovieDescription(Action<string, bool> speak)
        : this(speak, () => DateTime.UtcNow)
    {
    }

    public OpeningMovieDescription(Action<string> speak, Func<DateTime> utcNow)
        : this((text, _) => speak(text), utcNow)
    {
    }

    public OpeningMovieDescription(Action<string, bool> speak, Func<DateTime> utcNow)
    {
        this.speak = speak;
        this.utcNow = utcNow;
    }

    public bool IsRunning => startedAt != DateTime.MinValue;

    public double ElapsedSeconds => startedAt == DateTime.MinValue
        ? 0
        : (utcNow() - startedAt).TotalSeconds;

    public void Start()
    {
        startedAt = utcNow();
        nextCueIndex = 0;
        speak("Opening movie audio description started.", true);
        Tick(force: true);
    }

    public void Stop()
    {
        startedAt = DateTime.MinValue;
        nextCueIndex = 0;
    }

    public void Tick(bool force = false)
    {
        if (startedAt == DateTime.MinValue)
        {
            return;
        }

        var elapsed = ElapsedSeconds;
        while (nextCueIndex < Cues.Count && (force || elapsed >= Cues[nextCueIndex].Seconds))
        {
            speak(Cues[nextCueIndex].Text, false);
            nextCueIndex++;
            force = false;
        }

        if (nextCueIndex >= Cues.Count && elapsed > Cues[^1].Seconds + 8)
        {
            startedAt = DateTime.MinValue;
        }
    }
}

public readonly record struct OpeningMovieCue(int Seconds, string Text);
