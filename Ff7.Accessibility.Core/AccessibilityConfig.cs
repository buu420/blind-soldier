namespace Ff7.Accessibility.Core;

public sealed class AccessibilityConfig
{
    public string GameLanguage { get; set; } = "auto";
    public bool EnableSpeech { get; set; } = true;
    public bool EnableRuntimeMenuSpeech { get; set; } = true;
    public bool EnableRuntimeDialogueSpeech { get; set; } = true;
    public bool EnableNativeSystemMenuSpeech { get; set; } = true;
    public int NativeSystemMenuHelpDelayMs { get; set; } = 500;
    public bool EnableFfnxPopupSpeech { get; set; } = true;
    public bool SpeakOnLoad { get; set; } = true;
    public bool EnableTitleMenuVisualReader { get; set; } = false;
    public int TitleMenuScanIntervalMs { get; set; } = 180;
    public bool EnableOpeningMovieDescription { get; set; } = false;
    public int OpeningMovieProbeIntervalMs { get; set; } = 250;
    public bool EnableOpeningMovieAudioTrack { get; set; } = true;
    public string OpeningMovieAudioTrackPath { get; set; } = @"Assets\movies\opening_audio_description.ogg";
    public int OpeningMovieAudioTrackVolumePercent { get; set; } = 300;
    public bool EnableFieldMessageReader { get; set; } = true;
    public bool SpeakFieldMessages { get; set; } = true;
    public int FieldMessageScanIntervalMs { get; set; } = 120;
    public int FieldMessageStableMs { get; set; } = 450;
    public bool EnableFieldMessageWindowDiagnostics { get; set; } = true;
    public bool EnableFieldMessageOpenHook { get; set; } = true;
    public bool EnableFieldMessageOpenDiagnostics { get; set; } = true;
    public bool EnableFieldMessagePreviewHook { get; set; } = true;
    public bool EnableFieldMessagePreviewDiagnostics { get; set; } = true;
    public bool EnableFieldOpcodeMessageHooks { get; set; } = true;
    public bool EnableFieldOpcodeMessageDiagnostics { get; set; } = true;
    public bool EnableFieldCutsceneDescriptions { get; set; } = true;
    public bool EnableFieldCutsceneDescriptionDiagnostics { get; set; } = true;
    // The Speed Square coaster draws the player's own sight and charge on screen.
    // Reading them aloud gives a blind player the same facts; it never presses a
    // button.
    // The submarine mission is its own module with its own instruments: a clock, a
    // damage bar, depth and speed numbers, a compass and pitch gauge, four torpedo
    // lamps, warning lamps, and coloured squares over the enemies it has detected and
    // is actually drawing. Reading those aloud gives a blind player the same screen.
    // Nothing here fires, steers, or reports anything the mission does not draw.
    // Reactor 5's three buttons have to be pushed together. The game says so in its own
    // words and then shows Barret and Tifa raising their arms; this puts a tone on the
    // start of that raise so a blind player can push with them. It never presses
    // anything and never reads the hidden success byte.
    public bool EnableReactor5ButtonCue { get; set; } = true;
    public string Reactor5ButtonCueSoundPath { get; set; } = ArcadeCueAssets.FieldActivityButtonReady;
    public int Reactor5ButtonCueVolumePercent { get; set; } = 100;
    public bool EnableSubmarineMissionReadout { get; set; } = true;
    public bool EnableSubmarineMissionDiagnostics { get; set; } = false;
    // The lock cue goes to its own device, because the mission is played with a button
    // held down and the moment a target becomes the one the torpedoes will fire at is
    // exactly the signal a held press must not be able to cut off. It defaults to the
    // existing two-pip cue rather than a wave this build does not ship.
    public string SubmarineMissionLockCueSoundPath { get; set; } = ArcadeCueAssets.FieldActivityButtonReady;
    public int SubmarineMissionCueVolumePercent { get; set; } = 70;
    public bool EnableSpeedSquareCoasterReadout { get; set; } = true;
    public bool EnableSpeedSquareCoasterDiagnostics { get; set; } = false;
    // The Basketball Game's wind-up. The tick and the top-of-rise marker play on
    // their own devices so the button the player is holding cannot cut them off.
    // The native activities between the Temple of the Ancients and Icicle Inn: the
    // rolling corridor, the clock, the chase, the Bone Village dig, the pillar jumps
    // and the altar scene. Each of these is something a sighted player watches or is
    // asked to press, and none of them is a doorway the route planner could offer.
    public bool EnableFieldActivityReadout { get; set; } = true;
    // The button-ready cue plays on its own device, because the player asked for a
    // signal that a held or repeated button press cannot cut off.
    public string FieldActivityButtonReadyCueSoundPath { get; set; } = ArcadeCueAssets.FieldActivityButtonReady;
    public int FieldActivityCueVolumePercent { get; set; } = 70;
    public bool EnableWonderSquareBasketballCues { get; set; } = true;
    public bool EnableWonderSquareBasketballDiagnostics { get; set; } = false;
    // Dedicated cues rather than borrowed navigation waves: a 32 ms 550 Hz tick for
    // the rise and a distinctly higher 95 ms 1100 Hz tone for the settled pose, so
    // the two are not mistaken for each other or for a footstep.
    public string WonderSquareBasketballWindUpCueSoundPath { get; set; } = ArcadeCueAssets.BasketballRiseTick;
    public string WonderSquareBasketballTopCueSoundPath { get; set; } = ArcadeCueAssets.BasketballPoseTop;
    public int WonderSquareBasketballCueVolumePercent { get; set; } = 100;
    // The chocobo racing betting menu, race strip and results screen, each read as
    // it is drawn. Nothing here selects a cell, buys a ticket, or reads an attribute
    // the open card does not show.
    public bool EnableChocoboSquareReadout { get; set; } = true;
    public bool EnableChocoboSquareDiagnostics { get; set; } = false;

    // Where the Shooting Coaster's currently rendered targets are relative to the
    // player's own sight, plus the displayed score and resolved hits. The direction
    // also plays as a spatial cue, because the fire button is pressed constantly and
    // each press interrupts screen-reader speech.
    public bool EnableSpeedSquareCoasterTargetCues { get; set; } = true;
    public int SpeedSquareCoasterTargetCueVolumePercent { get; set; } = 100;

    // Which way the locked arms are leaning during an Arm Wrestling bout. The
    // contest's own instructions ask for continuous [OK] presses, and each press
    // interrupts screen-reader speech, so the pose change is also carried by three
    // short tones on their own device: higher means their arm is going down, lower
    // means yours is, and the middle tone is level.
    public bool EnableWonderSquareArmWrestlingCues { get; set; } = true;
    public string WonderSquareArmWrestlingLevelCueSoundPath { get; set; } = ArcadeCueAssets.ArmWrestlingLevel;
    public string WonderSquareArmWrestlingPushAheadCueSoundPath { get; set; } = ArcadeCueAssets.ArmWrestlingPushAhead;
    public string WonderSquareArmWrestlingPushedBackCueSoundPath { get; set; } = ArcadeCueAssets.ArmWrestlingPushedBack;
    public int WonderSquareArmWrestlingCueVolumePercent { get; set; } = 100;
    // Who scored and what the round score is during a 3D Battler match. The
    // opponent's move is never reported: the script rolls it before reading the
    // player's input, so it is not on screen at that point.
    public bool EnableWonderSquare3DBattlerCues { get; set; } = true;
    public bool EnableWonderSquare3DBattlerDiagnostics { get; set; } = false;
    // Described in-game films play their narration on a separate audio device so an
    // ordinary button press cannot cut a 45-second description into a fragment.
    // Disabling this keeps the single spoken paragraph the mod used before.
    public bool EnableFieldMovieNarrationTracks { get; set; } = true;
    public string FieldMovieNarrationTrackDirectory { get; set; } = @"Assets\movies";

    /// <summary>
    /// 100, not the opening film's 300. These recordings were normalised on their
    /// own: the player applies linear gain with no limiter, and an offline decode of
    /// all six shows every one of them clipping at 300 - gold1 peaks at 2.65 with
    /// over twenty-three thousand samples past full scale, and the tour films at
    /// 1.74..1.93. At 100 they all sit below full scale. The opening film's own
    /// setting is a separate key and is left alone.
    /// </summary>
    public int FieldMovieNarrationTrackVolumePercent { get; set; } = 100;
    public int FieldMessageOpenSpeechSettleMs { get; set; } = 0;
    public bool EnableFieldDialogueDrawSpeech { get; set; } = true;
    public int FieldDialogueDrawStableMs { get; set; } = 250;
    public bool EnableNameEntryMenuSpeech { get; set; } = true;
    public bool EnableNameEntryMenuDiagnostics { get; set; } = true;
    public int NameEntryMenuSpeechSettleMs { get; set; } = 0;
    public bool EnableFieldFootstepFeedback { get; set; } = true;
    public bool EnableFieldPositionDiagnostics { get; set; } = true;
    public int FieldFootstepScanIntervalMs { get; set; } = 80;
    public int FieldFootstepWalkIntervalMs { get; set; } = 500;
    public int FieldFootstepRunIntervalMs { get; set; } = 300;
    public int FieldFootstepMeasuredRunSpeedUnitsPerSecond { get; set; } = 300;
    public int FieldFootstepVolumePercent { get; set; } = 200;
    public string FieldFootstepSoundPath { get; set; } = @"Assets\footsteps\selected_subway_step.ogg";
    public bool UseCosmoFootstepSounds { get; set; } = true;
    public string CosmoFootstepSoundDirectory { get; set; } = @"Assets\footsteps\cosmo";
    public bool PlayFootstepProbeOnLoad { get; set; } = false;
    public int FieldFootstepProbeDelayMs { get; set; } = 1000;
    public bool EnableFieldFootstepDistanceProbe { get; set; } = true;
    public int FieldFootstepDistanceProbeReportSamples { get; set; } = 8;
    public bool EnableFieldZoneTransitionCue { get; set; } = false;
    public int FieldZoneTransitionCueSettleMs { get; set; } = 300;
    public int FieldZoneTransitionCueVolumePercent { get; set; } = 100;
    public string FieldZoneTransitionCueSoundPath { get; set; } = @"Assets\navigation\field_zone_transition.wav";
    public bool EnableFieldExitProximityCues { get; set; } = true;
    public int FieldExitCueInnerRangeUnits { get; set; } = 80;
    public int FieldExitCueOuterRangeUnits { get; set; } = 400;
    public int FieldExitCueIntervalMs { get; set; } = 3200;
    public int FieldExitCueVolumePercent { get; set; } = 100;
    public string FieldExitCueSoundPath { get; set; } = @"Assets\navigation\field_zone_transition.wav";
    public bool EnableFieldLadderProximityCues { get; set; } = true;
    public int FieldLadderCueInnerRangeUnits { get; set; } = 80;
    public int FieldLadderCueOuterRangeUnits { get; set; } = 400;
    public int FieldLadderCueIntervalMs { get; set; } = 1600;
    public int FieldLadderCueVolumePercent { get; set; } = 100;
    public string FieldLadderCueSoundPath { get; set; } = @"Assets\navigation\ladder_061.wav";
    public int FieldLadderMountCueIntervalMs { get; set; } = 700;
    public int FieldLadderMountCueVolumePercent { get; set; } = 100;
    public string FieldLadderMountCueSoundPath { get; set; } = @"Assets\navigation\ladder_approach_214.wav";
    public bool EnableFieldSwingingBarTimingCue { get; set; } = true;
    public int FieldSwingingBarTimingCueVolumePercent { get; set; } = 100;
    public string FieldSwingingBarTimingCueSoundPath { get; set; } = @"Assets\navigation\swing_jump_058.wav";
    public bool EnableSquatMinigamePrompts { get; set; } = true;
    public bool EnableJunonMinigamePrompts { get; set; } = true;
    public bool EnableJunonTimingCue { get; set; } = true;
    public int JunonTimingCueVolumePercent { get; set; } = 100;
    public string JunonTimingCueSoundPath { get; set; } = @"Assets\navigation\swing_jump_058.wav";
    public bool EnableJunonParadeAlignmentAssist { get; set; } = true;
    public bool EnableFloor60SoldierTurnCue { get; set; } = true;
    public int Floor60SoldierTurnCueVolumePercent { get; set; } = 100;
    public int Floor60StatueBeaconIntervalMs { get; set; } = 500;
    public int Floor60StatueArrivalDistanceUnits { get; set; } = 60;
    public int Floor60GuardReactionLeadMilliseconds { get; set; } = 500;
    public string Floor60SoldierTurnCueSoundPath { get; set; } = @"Assets\navigation\floor60_statue_134.wav";
    public bool EnableHighwayAccessibility { get; set; } = true;
    public bool EnableHighwayAutoSteering { get; set; } = true;
    public bool EnableHighwaySteeringGuidance { get; set; } = true;
    public int HighwayCueVolumePercent { get; set; } = 100;
    public int HighwayEnemyCueIntervalMs { get; set; } = 320;
    public int HighwayTruckBeaconIntervalMs { get; set; } = 320;
    public int HighwaySteeringCueIntervalMs { get; set; } = 700;
    public int HighwayCriticalSteeringCueIntervalMs { get; set; } = 260;
    public int HighwayComfortableTruckDistanceUnits { get; set; } = 500;
    public int HighwayTruckThreatDistanceUnits { get; set; } = 300;
    public int HighwayDistanceWarningUnits { get; set; } = 1200;
    public int HighwayDistanceWarningRecoveryUnits { get; set; } = 900;
    public string HighwayLowerPriorityCueSoundPath { get; set; } = @"Assets\highway\enemy_lower_priority_058.wav";
    public string HighwayImportantCueSoundPath { get; set; } = @"Assets\highway\enemy_important_059_short.wav";
    public string HighwayTruckBeaconSoundPath { get; set; } = @"Assets\highway\truck_beacon_478.wav";
    public string HighwaySteeringCueSoundPath { get; set; } = @"Assets\navigation\navigation_beacon_214_remix.wav";
    public bool EnableFieldNavigationAssistant { get; set; } = true;
    public bool EnableFieldNavigationDiagnostics { get; set; } = true;
    public int FieldNavigationScanIntervalMs { get; set; } = 50;
    public int FieldNavigationSpeechIntervalMs { get; set; } = 1000;
    public int FieldNavigationRunningSpeechIntervalMs { get; set; } = 350;
    public int FieldNavigationSpeechDistanceUnitsPerCount { get; set; } = 60;
    public int FieldNavigationArrivalDistanceUnits { get; set; } = 80;
    public bool EnableNavigationProgressIndicators { get; set; } = true;
    public int NavigationProgressIntervalPercent { get; set; } = 5;
    public bool EnableWorldMapFootstepFeedback { get; set; } = true;
    public bool EnableWorldMapNavigationAssistant { get; set; } = true;
    public bool EnableWorldMapNavigationDiagnostics { get; set; } = true;
    public bool EnableWorldMapEntranceProximityCues { get; set; } = true;
    public int WorldMapEntranceCueInnerRangeUnits { get; set; } = 512;
    public int WorldMapEntranceCueOuterRangeUnits { get; set; } = 4096;
    public int WorldMapEntranceCueIntervalMs { get; set; } = 3200;
    public int WorldMapEntranceCueVolumePercent { get; set; } = 100;
    public string WorldMapEntranceCueSoundPath { get; set; } = @"Assets\navigation\field_zone_transition.wav";
    public int WorldMapScanIntervalMs { get; set; } = 50;
    public int WorldMapFootstepWalkIntervalMs { get; set; } = 300;
    public int WorldMapFootstepChocoboIntervalMs { get; set; } = 500;
    public int WorldMapNavigationSpeechIntervalMs { get; set; } = 1000;
    public int WorldMapNavigationSpeechDistanceUnitsPerCount { get; set; } = 512;
    public bool EnableFieldObjectProximityCues { get; set; } = true;
    public int FieldObjectCueInnerRangeUnits { get; set; } = 80;
    public int FieldObjectCueOuterRangeUnits { get; set; } = 400;
    public int FieldObjectCueClusterRadiusUnits { get; set; } = 40;
    public int FieldObjectCueIntervalMs { get; set; } = 1000;
    public int FieldObjectCueVolumePercent { get; set; } = 100;
    public bool EnableMainMenuReader { get; set; } = true;
    public bool SpeakMainMenuSelections { get; set; } = true;
    public int MainMenuScanIntervalMs { get; set; } = 50;
    public int MainMenuSpeechSettleMs { get; set; } = 0;
    public bool EnableRenderedMenuTextSpeech { get; set; } = true;
    public int RenderedMenuTextSpeechSettleMs { get; set; } = 0;
    public bool EnableExperimentalHooks { get; set; } = true;
    public bool EnableMenuTextRenderDiagnostics { get; set; } = true;
    public bool EnableInGameMenuTextDrawDiagnostics { get; set; } = true;
    public bool EnableInGameMenuTextDrawSpeech { get; set; } = false;

    /// <summary>
    /// Samples the data segment while the Fort Condor battle is running so new
    /// state can be located. A research instrument, off unless someone is
    /// deliberately using it.
    /// </summary>
    /// <remarks>
    /// It found the cursor, the Setting Menu and the unit table, and
    /// <see cref="Ff7.Accessibility.Reloaded"/>'s battle reader was built on what
    /// it found - but it was left on, still wired to real speech. During a battle
    /// it queues raw diagnostics ("428, 706", "Unit 255.", "cursor") plus a
    /// duplicate of every hire line, sixteen utterances in the worst second,
    /// against the reader's own. Because they queue rather than interrupt, the
    /// backlog never drains and the menu option under the cursor arrives long
    /// after the player has moved past it. Reported 2026-08-22 as the Setting
    /// Menu reading its title and then none of its options.
    ///
    /// <para>It stays in the tree because the enemy unit names - Beast,
    /// Barbarian, Wyvern, Commander - are still unmapped and this is the
    /// instrument that would close that gap. Turn it on for that, not for play.</para>
    /// </remarks>
    public bool EnableCondorMinigameProbe { get; set; } = false;
    public int CondorMinigameProbeIntervalMs { get; set; } = 120;
    public bool EnableCondorBattleLineAnnouncements { get; set; } = true;
    public bool EnableCondorEnemyArrivalAnnouncements { get; set; } = true;
    public bool EnableTitleMenuNativeCursorSpeech { get; set; } = true;
    public bool EnableTitleMenuNativeCursorDiagnostics { get; set; } = true;
    public int TitleMenuNativeCursorSettleMs { get; set; } = 0;
    public bool EnableTitleLoadMenuSpeech { get; set; } = true;
    public int TitleLoadMenuSpeechSettleMs { get; set; } = 40;
    public bool EnableMenuWidgetDiagnostics { get; set; } = true;
    public bool EnableInGameMenuHelpTextSpeech { get; set; } = true;
    public bool EnableInGameMenuWidgetSpeech { get; set; } = true;
    public int InGameMenuSpeechSettleMs { get; set; } = 0;
    public int MenuTextRenderDiagnosticsDedupMs { get; set; } = 750;
    public bool EnableBattleMenuSpeech { get; set; } = true;
    public bool EnableBattleTargetSpeech { get; set; } = true;
    public bool EnableBattleMessageSpeech { get; set; } = true;
    public bool EnableBattleResultsSpeech { get; set; } = true;
    public bool EnableBattleDamageSpeech { get; set; } = true;
    public bool EnableBattleEncounterSpeech { get; set; } = true;
    public bool EnableBattleEnemyActionSpeech { get; set; } = true;
    public bool EnableBattleStatusSpeech { get; set; } = true;
    public bool EnableBattleDiagnostics { get; set; } = true;
}


