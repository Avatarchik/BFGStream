﻿using Assets;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

public class GameScript : MonoBehaviour {
    public static Dictionary<string, string> CONFIG;
    static GameScript() {
        CONFIG = new Dictionary<string, string>();
        DirectoryInfo configDirectory = new DirectoryInfo(Application.streamingAssetsPath);
        FileInfo[] configFiles = configDirectory.GetFiles("config.ini");
        if (configFiles.Length == 0) {
            throw new Exception("Config file not found!");
        }
        using (StreamReader sr = configFiles[0].OpenText()) {
            string s = "";
            while ((s = sr.ReadLine()) != null) {
                s = s.Trim();
                if (s.Length == 0) {
                    continue;
                }
                int commentCol = s.IndexOf('#');
                if (commentCol > -1) {
                    s = s.Substring(0, commentCol);
                }
                string[] tokens = s.Split('=');
                if (tokens.Length != 2) {
                    throw new Exception(string.Format("Malformed config line: {0}", s));
                }
                CONFIG[tokens[0].Trim()] = tokens[1].Trim();
            }
        }
    }

    static readonly string[] ROUND_NAMES = new string[] { "Nonesie", "Onesie", "Twofer", "Threepeat", "Four Score", "Five Guys", "Six Pack", "Seventh Heaven", "Eight Ball", "Nine Iron",
                                                          "Ten Speed", "Elevensies", "Ocean's Twelve", "Lucky Thirteen", "February Fourteen", "Fifteen Puzzle", "Sweet Sixteen", "Seventeen Magazine", "Eighteen Holes", "Hey Nineteen",
                                                          "Twentsie", "Forever Twenty-One", "Catch Twenty-Two", "Twenty-Three Skidoo", "Twenty-Four Karat", "Twenty-Five Cents", "Twenty-Six Letters", "Twenty-Seven Dresses", "Twenty-Eight Days Later", "Refinery Twenty-Nine",
                                                          "Thirtsie", "Thirty-One Flavors", "Fat Thirty-Two" };
    static readonly int[] ROUND_POINTS = new int[] { 2000, 400, 300, 250, 225, 200, 180, 160, 150, 140, 130, 120, 110, 100, 90, 80, 70, 60, 50 };
    static readonly float FINALIZE_TIMER_SECONDS = 24;
    static readonly float FINALIZE_TIMER_LIGHTNING_ROUND_SECONDS = 40;
    public static Color SUB_COLOR = new Color(1, 0.6471f, 0.7059f);
    public static string SUB_COLOR_HEX_STRING = "#" + ColorUtility.ToHtmlStringRGB(GameScript.SUB_COLOR);
    public static int BASE_VIEWER_POPUP_COUNT = 8;
    public static Regex WORD_REGEX = new Regex("[^a-z0-9]");

    public static Dictionary<string, string> viewerTitles = new Dictionary<string, string>();
    public bool spinnerOn;

    public GameObject botPrefab;
    public BotScript botScript;
    public DBScript dbScript;
    public ToastsScript toastsScript;

    public GameObject ui, viewerPopups, pointFloaters;
    // Bottom panel.
    public GameObject newestWordPrefab, viewerPopupPrefab, pointFloaterPrefab;
    public TextMeshProUGUI[] playerLabels2, playerLabels4;
    public TextMeshProUGUI[] scoreLabels2, scoreLabels4;
    public LockScript[] lockScripts2, lockScripts4;
    public Sprite[] playerAvatars;
    TextMeshProUGUI[] playerLabels;
    TextMeshProUGUI[] scoreLabels;
    LockScript[] lockScripts;
    NewestWordScript[] newestWordScripts;
    // Top panel.
    public TextMeshProUGUI roundTMP, roundPointsTMP, viewersTMP;
    // Timer.
    public TextMeshProUGUI timerTMP;
    public CanvasGroup timerGroup;
    // Leaderboard panel.
    public GameObject leaderboardRowPrefab;
    public GameObject leaderboardAnchor;
    // Words panel.
    public GameObject wordsPanel, wordsBanner, wordsTexts;
    public TextMeshProUGUI wordsTMP;
    // Fullscreen wipe leaderboard.
    public WipeScript wipeScript;
    // Particles.
    public ParticleSystem confetti, streamers;
    // Sound.
    public AudioSource sfxLock, sfxWin, sfxCountdown, sfxLightningIntro, sfxLightningLoop, sfxLightningWin;
    float countdownVolume;
    // Text resources.
    public TextAsset lemmas, dictionaryAsset;
    HashSet<Tuple<string, string>> lemmaMatches;
    Dictionary<string, string> dictionaryDefinitions;

    // Game configuration.
    public List<string> players;
    List<string> displayNames;

    public List<string[]> words;
    string[] pendingWords;
    Dictionary<string, string> viewerWords, lastViewerWords;
    int[] scores, lastScores;
    RollingScores viewerScores;
    HashSet<string> doubledUpViewers, lastDoubledUpViewers;
    Dictionary<string, int> viewerStreaks;
	HashSet<string> viewersMatchedToday;
    HashSet<string> viewersFollowedToday;
    public HashSet<string> subscribers;
    bool[] pendingLastFrame;
    public bool gameWon;
    bool finalizeTimerActive;
    public float finalizeTimer;
    public ViewerPopupScript[] viewerPopupScripts;
    bool freezeLeaderboard;
    int logFileSuffix;
    public bool lightningRound;

    void Start() {
        players = CONFIG["host_usernames"].Split(',').ToList();
        displayNames = CONFIG["host_display_names"].Split(',').ToList();

        wordsPanel.transform.localPosition = new Vector3(1300, 470, 0);
        wordsTMP.text = "";
        countdownVolume = sfxCountdown.volume;
        foreach (TextMeshProUGUI playerLabel in playerLabels2) {
            playerLabel.transform.parent.gameObject.SetActive(false);
        }
        foreach (TextMeshProUGUI playerLabel in playerLabels4) {
            playerLabel.transform.parent.gameObject.SetActive(false);
        }

        newestWordScripts = new NewestWordScript[players.Count];

        words = new List<string[]>();
        pendingWords = new string[players.Count];
        viewerWords = new Dictionary<string, string>();
        lastViewerWords = new Dictionary<string, string>();
        scores = new int[players.Count];
        lastScores = new int[players.Count];
        viewerScores = new RollingScores(3600);
        doubledUpViewers = new HashSet<string>();
        lastDoubledUpViewers = new HashSet<string>();
        viewerStreaks = new Dictionary<string, int>();
		viewersMatchedToday = new HashSet<string>();
        viewersFollowedToday = new HashSet<string>();
        subscribers = new HashSet<string>();
        pendingLastFrame = new bool[players.Count];
        viewerPopupScripts = new ViewerPopupScript[players.Count];
        try {
            LoadScores();
        } catch (Exception e) {
            Debug.Log(string.Format("Loading viewer scores failed: {0}", e));
        }
        LoadLemmas(); // TODO: Once this is tested, disable in the editor.
        LoadDictionary();
    }

    void Update() {
        float time = Time.time;

        // Update player UI references.
        if (playerLabels == null || playerLabels.Length != players.Count) {
            if (playerLabels != null) {
                foreach (TextMeshProUGUI playerLabel in playerLabels) {
                    playerLabel.transform.parent.gameObject.SetActive(false);
                }
            }
            if (players.Count == 2) {
                playerLabels = playerLabels2;
                scoreLabels = scoreLabels2;
                lockScripts = lockScripts2;
            } else if (players.Count == 3) {
                playerLabels = new TextMeshProUGUI[] { playerLabels2[0], playerLabels4[2], playerLabels4[3] };
                scoreLabels = new TextMeshProUGUI[] { scoreLabels2[0], scoreLabels4[2], scoreLabels4[3] };
                lockScripts = new LockScript[] { lockScripts2[0], lockScripts4[2], lockScripts4[3] };
            } else if (players.Count == 4) {
                playerLabels = playerLabels4;
                scoreLabels = scoreLabels4;
                lockScripts = lockScripts4;
            }
            for (int i = 0; i < playerLabels.Length; i++) {
                playerLabels[i].transform.parent.gameObject.SetActive(true);
                playerLabels[i].text = displayNames[i];
                Sprite avatar = null;
                foreach (Sprite s in playerAvatars) {
                    if (s.name == players[i]) {
                        avatar = s;
                        break;
                    }
                }
                playerLabels[i].transform.parent.GetChild(0).GetChild(0).GetComponent<SpriteRenderer>().sprite = avatar;
            }
            for (int i = 0; i < lockScripts.Length; i++) {
                lockScripts[i].Set(pendingWords[i] != null);
            }
        }

        if (!freezeLeaderboard) {
            viewerScores.Update(time);
        }
        if (botScript.subscribers != null) {
            lock (botScript.subscribers) {
                subscribers.UnionWith(botScript.subscribers);
                botScript.subscribers.Clear();
            }
        }

        // DEBUG DEBUG DEBUG
        if (Input.GetKeyDown(KeyCode.A)) {
            viewerScores.Award("user" + (viewerScores.NumViewers() + 1), 25 * UnityEngine.Random.Range(1, 11), time);
        }
        if (Input.GetKeyDown(KeyCode.Q)) {
            if (wordsTMP.text.Length > 0) {
                wordsTMP.text += "\n";
            }
            wordsTMP.text += new string('Q', UnityEngine.Random.Range(1, 15)) + " / " + new string('Q', UnityEngine.Random.Range(1, 15));
        }
        if (Input.GetKeyDown(KeyCode.Z)) {
            toastsScript.Toast((ToastType)UnityEngine.Random.Range(0, 14), string.Join(" ", Enumerable.Repeat("blah", UnityEngine.Random.Range(1, 20))));
        }
        if (Input.GetKeyDown(KeyCode.Alpha1)) {
            pendingWords[0] = pendingWords[0] == null ? "sandwich" : null;
        }
        if (Input.GetKeyDown(KeyCode.Alpha2)) {
            pendingWords[1] = pendingWords[1] == null ? "sandwich" : null;
        }
        if (Input.GetKeyDown(KeyCode.Alpha3) && players.Count >= 3) {
            pendingWords[2] = pendingWords[2] == null ? "sandwich" : null;
        }
        if (Input.GetKeyDown(KeyCode.Alpha4) && players.Count >= 4) {
            pendingWords[3] = pendingWords[3] == null ? "sandwich" : null;
        }
        if (Input.GetKeyDown(KeyCode.Alpha5)) {
            pendingWords[0] = pendingWords[0] == null ? "panini" : null;
        }
        if (Input.GetKeyDown(KeyCode.Alpha6)) {
            pendingWords[1] = pendingWords[1] == null ? "hoagie" : null;
        }
        if (Input.GetKeyDown(KeyCode.Alpha7) && players.Count >= 3) {
            pendingWords[2] = pendingWords[2] == null ? "grinder" : null;
        }
        if (Input.GetKeyDown(KeyCode.Alpha8) && players.Count >= 4) {
            pendingWords[3] = pendingWords[3] == null ? "sub" : null;
        }
        if (Input.GetKeyDown(KeyCode.S)) {
            lock (botScript.events) {
                botScript.events.Add(new Event(EventType.SUBSCRIPTION, "jim", null, "2", null));
            }
        }
        if (Input.GetKeyDown(KeyCode.B)) {
            lock (botScript.events) {
                botScript.events.Add(new Event(EventType.BITS, "barbobarbo", "101", "Tom—you're the man!"));
            }
        }
        if (Input.GetKeyDown(KeyCode.N)) {
            lock (botScript.events) {
                botScript.events.Add(new Event(EventType.BITS, "barbobarbo", "420", "julie, Sam, Will, tom... you're all awesome!"));
            }
        }
        if (Input.GetKeyDown(KeyCode.M)) {
            lock (botScript.events) {
                botScript.events.Add(new Event(EventType.BITS, "barbobarbo", "69", "I love this game!"));
            }
        }
        if (Input.GetKeyDown(KeyCode.Comma)) {
            lock (botScript.events) {
                botScript.events.Add(new Event(EventType.PUNISH, "barbobarbo", "Tom and Will", "true"));
            }
        }
        if (Input.GetKeyDown(KeyCode.D)) {
            lock (botScript.events) {
                botScript.events.Add(new Event(EventType.DOUBLE_UP, "wurstwurstwurstwurst7"));
            }
        }
        if (Input.GetKeyDown(KeyCode.E)) {
            for (int i = 0; i < 8; i++) {
                viewerWords.Add("wurstwurstwurstwurst" + i, "sandwich");
            }
        }
        // GUBED GUBED GUBED

        // Get inputs.
        if (Input.GetButtonDown("Start Timer")) {
            StartTimer(true);
        }
        if (spinnerOn) { // If the spinner is on, try to start the timer every frame until it works.
            StartTimer(false);
        }
        if (lightningRound && words.Count > 0 && !finalizeTimerActive && !gameWon) {
            StartTimer(false);
        }
        if (Input.GetButtonDown("Finalize Round") || (words.Count == 0 && !pendingWords.Contains(null))) {
            FinalizeRound();
        }
        if (Input.GetButtonDown("Lightning Round") && words.Count == 0) {
            lightningRound = true;
        }
        if (Input.GetButtonDown("Toggle Wipe")) {
            if (gameWon) {
                NewGame();
            }
            wipeScript.Toggle(viewerScores);
        }
        if (Input.GetButtonDown("New Game")) {
            NewGame();
        }
        if (Input.GetButtonDown("Claim Win") && players.Count == 2) {
            ClaimWin();
        }
        if (Input.GetButtonDown("Restart Bot")) {
            DestroyImmediate(botScript.gameObject);
            botScript = Instantiate(botPrefab).GetComponent<BotScript>();
        }
        BotUpdate();
        UpdateLeaderboard();

        // Update the bottom panel.
        for (int i = 0; i < players.Count; i++) {
            int displayedScore = int.Parse(scoreLabels[i].text);
            int delta = scores[i] - displayedScore;
            if (Mathf.Abs(delta) < 10) {
                scoreLabels[i].text = scores[i].ToString();
            } else if (delta != 0) {
                scoreLabels[i].text = Mathf.RoundToInt(displayedScore + delta * .1f).ToString();
            }
        }
        for (int i = 0; i < pendingWords.Length; i++) {
            bool playSFXLock = false;
            if ((pendingWords[i] == null) == pendingLastFrame[i]) {
                lockScripts[i].Set(!pendingLastFrame[i]);
                playSFXLock |= pendingWords[i] != null;
            }
            if (lightningRound && words.Count > 0) {
                playSFXLock = false;
            }
            if (playSFXLock) {
                sfxLock.PlayOneShot(sfxLock.clip);
            }
        }
        for (int i = 0; i < pendingWords.Length; i++) {
            pendingLastFrame[i] = pendingWords[i] != null;
        }
        // Update the top panel.
        int roundNumber = words.Count;
        if (gameWon) {
            roundNumber--;
        }
        roundTMP.text = roundNumber < ROUND_NAMES.Length ? ROUND_NAMES[roundNumber] : "Round " + roundNumber;
        int roundPoints = roundNumber == 0 ? 0 : GetPoints(roundNumber);
        roundPointsTMP.text = roundPoints + (roundPoints == 1 ? " point" : " points");
        if (words.Count == 0) {
            viewersTMP.text = "Starting a new game...";
        } else if (gameWon) {
            viewersTMP.text = "Victory!";
        } else {
            viewersTMP.text = viewerWords.Count + (viewerWords.Count == 1 ? " viewer locked in" : " viewers locked in");
        }
        // Update the timer.
        bool allWordsSubmitted = true;
        foreach (string pendingWord in pendingWords) {
            if (pendingWord == null) {
                allWordsSubmitted = false;
                break;
            }
        }
        if (finalizeTimerActive) {
            if (finalizeTimer > 0) {
                timerGroup.alpha = Mathf.Lerp(timerGroup.alpha, 1, .2f);
            } else {
                timerGroup.alpha = Mathf.Abs(finalizeTimer) % 1 > .5f ? 0 : 1;
            }
            sfxCountdown.volume = Mathf.Min(sfxCountdown.volume + .0015f, countdownVolume);
            finalizeTimer -= Time.deltaTime;
            if (finalizeTimer <= 0 && allWordsSubmitted) {
                FinalizeRound();
            }
        } else {
            timerGroup.alpha = Mathf.Lerp(timerGroup.alpha, 0, .2f);
        }
        int seconds = Mathf.CeilToInt(Mathf.Max(0, finalizeTimer));
        timerTMP.text = "00<voffset=0.125em>:</voffset>" + seconds.ToString("00");
        // Lightning round penalty.
        if (lightningRound && finalizeTimerActive && finalizeTimer <= 0) {
            for (int i = 0; i < pendingWords.Length; i++) {
                if (pendingWords[i] == null && scores[i] > 0) {
                    // TODO: Make this not framerate dependent.
                    scores[i]--;
                }
            }
        }
        // Lightning round music.
        if (lightningRound && words.Count > 0 && !gameWon) {
            if (!sfxLightningIntro.isPlaying && !sfxLightningLoop.isPlaying) {
                sfxLightningIntro.volume = .4f;
                sfxLightningLoop.volume = .4f;
                sfxLightningIntro.Play();
                sfxLightningLoop.PlayScheduled(AudioSettings.dspTime + sfxLightningIntro.clip.length);
            }
        } else if (sfxLightningIntro.isPlaying || sfxLightningLoop.isPlaying) {
            if (sfxLightningIntro.volume > 0) {
                sfxLightningIntro.volume -= .005f;
                sfxLightningLoop.volume -= .005f;
            } else {
                sfxLightningIntro.Stop();
                sfxLightningLoop.Stop();
            }
        }

        // Update the words panel.
        float wordsBannerTargetX, wordsTextsTargetX;
        if (wordsTMP.text.Length == 0) {
            wordsBannerTargetX = 0;
            wordsTextsTargetX = 0;
        }
        else {
            float wordsWidth = wordsTMP.preferredWidth;
            wordsWidth *= wordsTMP.fontSize / wordsTMP.fontSizeMax;
            wordsBannerTargetX = -wordsWidth - 150;
            wordsTextsTargetX = -wordsWidth / 2 - 375;
        }
        float wordsBannerX = Mathf.Lerp(wordsBanner.transform.localPosition.x, wordsBannerTargetX, .15f);
        wordsBanner.transform.localPosition = new Vector3(wordsBannerX, 0, 0);
        float wordsTextsX = Mathf.Lerp(wordsTexts.transform.localPosition.x, wordsTextsTargetX, .15f);
        wordsTexts.transform.localPosition = new Vector3(wordsTextsX, 0, 0);
        // Update particles.
        if (!gameWon && confetti.isPlaying) {
            confetti.Stop();
            streamers.Stop();
        }
    }

    void BotUpdate() {
        float time = Time.time;
        if (!gameWon) {
            lock (botScript.words) {
                foreach (var kvp in botScript.words) {
                    if (gameWon) {
                        botScript.Whisper(kvp.Key, "The game is already won! Wait for the next one to start.");
                        continue;
                    }
                    if (WordUsed(kvp.Value)) {
                        botScript.Whisper(kvp.Key, "That word has already been used during this game.");
                        continue;
                    }
                    int playerIndex = players.IndexOf(kvp.Key);
                    if (playerIndex == -1 && words.Count == 0) {
                        botScript.Whisper(kvp.Key, "The next game will start when the hosts pick their words.");
                        continue;
                    }
                    if (playerIndex >= 0) {
                        pendingWords[playerIndex] = kvp.Value;
                    } else {
                        botScript.Whisper(kvp.Key, viewerWords.ContainsKey(kvp.Key) ? "Your word has been updated." : "You're locked in!");
                        viewerWords[kvp.Key] = kvp.Value;
                        if (!viewerScores.SeenViewer(kvp.Key)) {
                            viewerScores.Award(kvp.Key, 0, time);
                            toastsScript.Toast(ToastType.NEW_PLAYER, string.Format("{0} submitted their first word!", GetUsernameString(kvp.Key)));
                        }
                    }
                }
                botScript.words.Clear();
            }
        }
        lock (botScript.adminCommands) {
            if (botScript.adminCommands.ContainsValue("!")) {
                StartTimer(true);
            }
            if (botScript.adminCommands.ContainsValue("!!")) {
                FinalizeRound();
            }
            if (botScript.adminCommands.ContainsValue("!n")) {
                NewGame();
            }
            if (botScript.adminCommands.ContainsValue("!w")) {
                ClaimWin();
            }
            if (botScript.adminCommands.ContainsValue("!l")) {
                wipeScript.Toggle(viewerScores);
            }
            if (botScript.adminCommands.ContainsValue("!s")) {
                botScript.Spam();
            }
            if (botScript.adminCommands.ContainsValue("!f")) {
                freezeLeaderboard = !freezeLeaderboard;
                botScript.WhisperAdmin(freezeLeaderboard ? "Leaderboard has been frozen." : "Leaderboard has been unfrozen.");
            }
            foreach (var kvp in botScript.adminCommands) {
                if (kvp.Value.StartsWith("!p ")) {
                    string[] tokens = kvp.Value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 2) {
                        tokens = new string[] { tokens[0], tokens[1], "100" };
                    }
                    int points = 0;
                    if (tokens.Length != 3 || !int.TryParse(tokens[2], out points)) {
                        botScript.Whisper(kvp.Key, "Usage: !p <word> <points>");
                    }
                    string word = Util.SanitizeWord(tokens[1]);
                    List<string> awardedViewers = new List<string>();
                    foreach (var wkvp in lastViewerWords) {
                        if (wkvp.Value == word) {
                            viewerScores.Award(wkvp.Key, points, time);
                            awardedViewers.Add(wkvp.Key);
                        }
                    }
                    if (awardedViewers.Count > 0) {
                        awardedViewers = awardedViewers.OrderByDescending(v => lastDoubledUpViewers.Contains(v)).ThenByDescending(v => subscribers.Contains(v)).ToList();
                        if (awardedViewers.Count > 4) {
                            int trunc = awardedViewers.Count - 3;
                            awardedViewers.RemoveRange(3, awardedViewers.Count - 3);
                            awardedViewers.Add(trunc + " others");
                        }
                        viewerScores.FinalizeScores();
                        if (points != 0) {
                            toastsScript.Toast(ToastType.AWARD, string.Format("{0} {1} awarded {2} {3} for submitting \"{4}\"!", JoinAwardeesGrammatically(awardedViewers.ToArray()), awardedViewers.Count == 1 ? "was" : "were", points, points == 1 ? "point" : "points", word.ToUpper()));
                        }
                    }
                } else if (kvp.Value.StartsWith("!w ")) {
                    string[] tokens = kvp.Value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length < 3) {
                        botScript.Whisper(kvp.Key, "Usage: !w <player index> <player index>...");
                        continue;
                    }
                    int[] indices = new int[tokens.Length - 1];
                    bool parseSuccess = true;
                    for (int i = 0; i < indices.Length; i++) {
                        int index;
                        parseSuccess &= int.TryParse(tokens[i + 1], out index);
                        if (!parseSuccess || index < 0 || index >= players.Count) {
                            break;
                        }
                        indices[i] = index;
                    }
                    if (!parseSuccess) {
                        botScript.Whisper(kvp.Key, "Error: Arguments must be integers within [0, numPlayers).");
                        continue;
                    }
                    ClaimWin(indices);
                } else if (kvp.Value.StartsWith("!u ")) {
                    string[] tokens = kvp.Value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length != 2) {
                        botScript.Whisper(kvp.Key, "Usage: !u <username>");
                        continue;
                    }
                    string username = tokens[1];
                    if (lastViewerWords.ContainsKey(username)) {
                        botScript.Whisper(kvp.Key, string.Format("{0}'s submission was: {1}", username, lastViewerWords[username]));
                    } else {
                        botScript.Whisper(kvp.Key, "That user didn't submit a word last round.");
                    }
                } else if (kvp.Value.StartsWith("!t ")) {
                    GrantTitle(kvp.Key, kvp.Value);
                } else if (kvp.Value.StartsWith("!ap ")) {
                    if (words.Count > 0 || !wipeScript.IsUp()) {
                        botScript.Whisper(kvp.Key, "This command can only be used in the nonesie round, with the fullscreen leaderboard up.");
                        continue;
                    }
                    string[] tokens = kvp.Value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length != 4) {
                        botScript.Whisper(kvp.Key, "Usage: !ap <index> <username> <displayname>");
                        continue;
                    }
                    int index;
                    if (!int.TryParse(tokens[1], out index) || index < 0 || index > players.Count) {
                        botScript.Whisper(kvp.Key, "Error: Invalid index.");
                    }
                    players.Insert(index, tokens[2]);
                    displayNames.Insert(index, tokens[3]);
                    pendingWords = new string[players.Count];
                } else if (kvp.Value.StartsWith("!rp ")) {
                    if (players.Count == 2) {
                        botScript.Whisper(kvp.Key, "You must have at least two players.");
                        continue;
                    }
                    if (words.Count > 0 || !wipeScript.IsUp()) {
                        botScript.Whisper(kvp.Key, "This command can only be used in the nonesie round, with the fullscreen leaderboard up.");
                        continue;
                    }
                    string[] tokens = kvp.Value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length != 2) {
                        botScript.Whisper(kvp.Key, "Usage: !rp <index>");
                        continue;
                    }
                    int index;
                    if (!int.TryParse(tokens[1], out index) || index < 0 || index >= players.Count) {
                        botScript.Whisper(kvp.Key, "Error: Invalid index.");
                    }
                    players.RemoveAt(index);
                    displayNames.RemoveAt(index);
                    pendingWords = new string[players.Count];
                } else if (kvp.Value.StartsWith("!d ")) {
                    string[] tokens = kvp.Value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length != 2) {
                        botScript.Whisper(kvp.Key, "Usage: !d <word to define>");
                        continue;
                    }
                    string word = tokens[1];
                    string definition = dbScript.QueryDefinition(word);
                    if (definition == null) {
                        botScript.Whisper(kvp.Key, "Couldn't find a definition for that word.");
                        continue;
                    }
                    toastsScript.Toast(ToastType.DICTIONARY, definition);
                }
            }
            botScript.adminCommands.Clear();
        }
        lock (botScript.viewerCommands) {
            foreach (var kvp in botScript.viewerCommands) {
                if (kvp.Value == "!score") {
                    if (!viewerScores.SeenViewer(kvp.Key)) {
                        botScript.Whisper(kvp.Key, "I haven't seen you before...");
                        continue;
                    }
                    botScript.Whisper(kvp.Key, string.Format("Your score in the last hour is {0}, and your total score is {1}.", viewerScores.GetScore(kvp.Key, true).ToString("N0"), viewerScores.GetScore(kvp.Key, false).ToString("N0")));
                }
            }
            botScript.viewerCommands.Clear();
        }
        lock (botScript.events) {
            foreach (Event e in botScript.events) {
                if (e.type == EventType.BITS) {
                    string user = e.info[0];
                    int bits = int.Parse(e.info[1]);
                    string message = e.info[2];
                    HashSet<int> matchingPlayerIndices = GetMatchingPlayerIndices(message);
                    int pointAward = matchingPlayerIndices.Count == 0 ? 0 : Mathf.FloorToInt(bits * 10 / matchingPlayerIndices.Count);
                    foreach (int matchingPlayerIndex in matchingPlayerIndices) {
                        scores[matchingPlayerIndex] += pointAward;
                        CreatePointFloater(matchingPlayerIndex, PointFloaterIcon.BITS, pointAward);
                    }
                    if (bits >= 50) {
                        string toastMessage;
                        if (matchingPlayerIndices.Count == 0) {
                            toastMessage = string.Format("{0} cheered {1} bits!", GetUsernameString(user), bits);
                        } else if (matchingPlayerIndices.Count == 1) {
                            toastMessage = string.Format("{0} cheered {1} bits for {2} — {2} gets {3} points!", GetUsernameString(user), bits, displayNames[matchingPlayerIndices.First()], pointAward);
                        } else {
                            string[] matchingDisplayNames = matchingPlayerIndices.Select(i => displayNames[i]).ToArray();
                            toastMessage = string.Format("{0} cheered {1} bits for {2} — they get {3} points each!", GetUsernameString(user), bits, Util.JoinGrammatically(matchingDisplayNames), pointAward);
                        }
                        if (bits == 69) {
                            toastMessage = toastMessage.Substring(0, toastMessage.Length - 1) + "... nice.";
                        } else if (bits == 420) {
                            toastMessage += " Blaze it!";
                        }
                        toastsScript.Toast(ToastType.BITS, toastMessage);
                    }
                } else if (e.type == EventType.DOUBLE_UP) {
                    string user = e.info[0];
                    if (players.Contains(user)) {
                        continue;
                    }
                    doubledUpViewers.Add(user);
                    toastsScript.Toast(ToastType.DOUBLE_UP, string.Format("{0} thinks they can double up!", GetUsernameString(user)));
                } else if (e.type == EventType.FOLLOW) {
                    string user = e.info[0];
                    if (!viewersFollowedToday.Contains(user)) {
                        toastsScript.Toast(ToastType.FOLLOW, string.Format("{0} followed the channel!", GetUsernameString(user)));
                        viewersFollowedToday.Add(user);
                    }
                } else if (e.type == EventType.PUNISH) {
                    string user = e.info[0];
                    if (players.Contains(user)) {
                        continue;
                    }
                    string message = e.info[1];
                    bool byPercentage = bool.Parse(e.info[2]);
                    HashSet<int> matchingPlayerIndices = GetMatchingPlayerIndices(message);
                    foreach (int i in matchingPlayerIndices) {
                        int deduction = byPercentage ? Mathf.CeilToInt(scores[i] * .1f / matchingPlayerIndices.Count) : Mathf.Min(scores[i], 1000 / matchingPlayerIndices.Count);
                        if (deduction == 0) {
                            toastsScript.Toast(ToastType.PUNISH, string.Format("{0} tried to punish {1}... but there was nothing left to take!", GetUsernameString(user), displayNames[i]));
                        } else {
                            toastsScript.Toast(ToastType.PUNISH, string.Format("{0} has punished {1}! \u2011{2} {3}!", GetUsernameString(user), displayNames[i], deduction, deduction == 1 ? "point" : "points"));
                            CreatePointFloater(i, PointFloaterIcon.PUNISH, -deduction);
                        }
                        scores[i] -= deduction;
                    }
                } else if (e.type == EventType.RECOUNT) {
                    string user = e.info[0];
                    if (players.Contains(user)) {
                        continue;
                    }
                    if (lastViewerWords.ContainsKey(user)) {
                        botScript.WhisperAdmin(string.Format("{0} demanded a recount for their word: {1}", user, lastViewerWords[user]));
                    } else {
                        botScript.WhisperAdmin(string.Format("{0} demanded a recount for their word, but we didn't receive one.", user));
                    }
                } else if (e.type == EventType.SUBSCRIPTION) {
                    string user = e.info[0];
                    string recipient = e.info[1];
                    string cumulative = e.info[2];
                    string streak = e.info[3];

                    if (recipient == null) {
                        subscribers.Add(user);
                    } else {
                        subscribers.Add(recipient);
                    }

                    if (user == null) {
                        toastsScript.Toast(ToastType.GIFT, string.Format("A kind stranger gifted a subscription to {0}!", GetUsernameString(recipient)));
                    } else if (recipient == null) {
                        if (cumulative == null) {
                            toastsScript.Toast(ToastType.SUB, string.Format("{0} subscribed!", GetUsernameString(user)));
                        } else {
                            toastsScript.Toast(ToastType.SUB, string.Format("{0} subscribed for {1} months!", GetUsernameString(user), cumulative));
                        }
                    } else {
                        toastsScript.Toast(ToastType.GIFT, string.Format("{0} gifted a subscription to {1}!", GetUsernameString(user), GetUsernameString(recipient)));
                    }
                }
            }
            botScript.events.Clear();
        }
    }
    void CreatePointFloater(int playerIndex, PointFloaterIcon icon, int points) {
        PointFloaterScript pointFloaterScript = Instantiate(pointFloaterPrefab, pointFloaters.transform).GetComponent<PointFloaterScript>();
        pointFloaterScript.Set(playerLabels[playerIndex], icon, points);
    }
    // Commands.
    void StartTimer(bool button) {
        if (finalizeTimerActive) {
            if (Application.isEditor) {
                finalizeTimer = .01f;
                sfxCountdown.Stop();
            }
            return;
        }
        if (gameWon) {
            FinalizeRound();
            return;
        }
        if (!lightningRound) {
            for (int i = 0; i < pendingWords.Length; i++) {
                if (pendingWords[i] == null) {
                    if (button && words.Count > 0) {
                        spinnerOn = !spinnerOn;
                    }
                    return;
                }
            }
        }
        if (words.Count == 0) {
            FinalizeRound();
            return;
        }
        finalizeTimer = lightningRound ? FINALIZE_TIMER_LIGHTNING_ROUND_SECONDS : FINALIZE_TIMER_SECONDS;
        finalizeTimerActive = true;
        botScript.Chat(string.Format("The next round begins in {0} seconds. What's the word between {1}? Whisper me your answer!", lightningRound ? FINALIZE_TIMER_LIGHTNING_ROUND_SECONDS : FINALIZE_TIMER_SECONDS, Util.JoinGrammatically(words[words.Count - 1].Select(s => s.ToUpper()).ToArray())), false);
        if (sfxCountdown.isPlaying) {
            sfxCountdown.Stop();
        }
        if (!lightningRound) {
            sfxCountdown.volume = 0;
            sfxCountdown.Play();
        }
        spinnerOn = false;
    }
    void FinalizeRound(bool claimed = false) {
        if (gameWon) {
            NewGame();
            return;
        }

        for (int i = 0; i < pendingWords.Length; i++) {
            if (pendingWords[i] == null) {
                return;
            }
        }
        words.Add(pendingWords);
        UpdateWords(claimed);
        pendingWords = new string[players.Count];
        lastViewerWords = viewerWords;
        viewerWords = new Dictionary<string, string>();
        finalizeTimerActive = false;
        try {
            SaveScores();
        } catch (Exception e) {
            Debug.Log(string.Format("Saving viewer scores failed: {0}", e));
        }
        botScript.Spam();
    }
    void NewGame() {
        words.Clear();
        wordsTMP.text = "";
        Array.Clear(pendingWords, 0, pendingWords.Length);
        viewerWords.Clear();
        lastViewerWords.Clear();
        for (int i = 0; i < newestWordScripts.Length; i++) {
            if (newestWordScripts[i] != null) {
                newestWordScripts[i].destroying = true;
            }
        }
        gameWon = false;
        lightningRound = false;
        finalizeTimer= 0;
        finalizeTimerActive = false;
        botScript.Spam();
    }
    void ClaimWin() {
        ClaimWin(Enumerable.Range(0, players.Count).ToArray());
    }
    void ClaimWin(int[] winnerIndices) {
        if (words.Count == 0) {
            return;
        }
        pendingWords = words[words.Count - 1];
        words.RemoveAt(words.Count - 1);
        // Find shortest word.
        string canonical = null;
        for (int i = 0; i < winnerIndices.Length; i++) {
            if (canonical == null || pendingWords[winnerIndices[i]].Length < canonical.Length) {
                canonical = pendingWords[winnerIndices[i]];
            }
        }
        // Canonicalize player words.
        HashSet<string> noncanonicals = new HashSet<string>();
        for (int i = 0; i < winnerIndices.Length; i++) {
            if (pendingWords[winnerIndices[i]] != canonical) {
                noncanonicals.Add(pendingWords[winnerIndices[i]]);
            }
            pendingWords[winnerIndices[i]] = canonical;
        }
        // Canonicalize viewer words.
        viewerWords = lastViewerWords;
        doubledUpViewers = lastDoubledUpViewers;
        string[] usernames = viewerWords.Keys.ToArray();
        foreach (string username in usernames) {
            if (noncanonicals.Contains(viewerWords[username])) {
                viewerWords[username] = canonical;
            }
        }
        scores = lastScores;
        viewerScores.Rollback();
        gameWon = false;
        FinalizeRound(true);
    }

    void UpdateLeaderboard() {
        // Get the N best players who have at least the third best distinct score.
        List<KeyValuePair<string, int>> orderedScores = viewerScores.GetRollingScoresDescending(subscribers);
        if (orderedScores.Count == 0) {
            return;
        }
        // Mark all existing rows as provisionally destroyed.
        foreach (Transform t in leaderboardAnchor.transform) {
            t.gameObject.GetComponent<LeaderboardRowScript>().ProvisionalDestroy();
        }
        int place = 0;
        for (int i = 0; i < orderedScores.Count; i++) {
            // Don't look at players past the third best distinct score, or past the 4th player, whichever is greater.
            if (i > 0 && orderedScores[i].Value != orderedScores[i - 1].Value) {
                place++;
                if (place == 3) {
                    break;
                }
            }
            if (i == 4) {
                break;
            }
            // If there's already a row for that player, update it.
            bool found = false;
            foreach (Transform t in leaderboardAnchor.transform) {
                GameObject child = t.gameObject;
                if (child.name != orderedScores[i].Key) {
                    continue;
                }
                found = true;
                LeaderboardRowScript leaderboardRowScript = child.GetComponent<LeaderboardRowScript>();
                leaderboardRowScript.Set(orderedScores[i].Key, place, i, orderedScores[i].Value, subscribers.Contains(orderedScores[i].Key));
                break;
            }
            if (found) {
                continue;
            }
            // Otherwise, make a new row for that player.
            GameObject newRow = Instantiate(leaderboardRowPrefab, leaderboardAnchor.transform);
            LeaderboardRowScript newScript = newRow.GetComponent<LeaderboardRowScript>();
            newScript.Set(orderedScores[i].Key, place, i, orderedScores[i].Value, subscribers.Contains(orderedScores[i].Key));
        }
    }
    void UpdateWords(bool claimed = false) {
        float time = Time.time;
        for (int i = 0; i < newestWordScripts.Length; i++) {
            if (newestWordScripts[i] != null) {
                newestWordScripts[i].destroying = true;
            }
        }
        string[] newestWords = words[words.Count - 1];
        Dictionary<string, List<int>> wordToPlayerIndex = new Dictionary<string, List<int>>();
        for (int i = 0; i < newestWords.Length; i++) {
            if (!wordToPlayerIndex.ContainsKey(newestWords[i])) {
                wordToPlayerIndex.Add(newestWords[i], new List<int>());
            }
            wordToPlayerIndex[newestWords[i]].Add(i);
        }
        string[] winningWords = wordToPlayerIndex.Where(kvp => kvp.Value.Count > 1).Select(kvp => kvp.Key).ToArray();
        for (int i = 0; i < newestWordScripts.Length; i++) {
            int winIndex = Array.IndexOf(winningWords, newestWords[i]);
            newestWordScripts[i] = Instantiate(newestWordPrefab, ui.transform).GetComponent<NewestWordScript>();
            newestWordScripts[i].Set(i, players.Count, newestWords[i], winIndex, winningWords.Length);
        }
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < words.Count - 1; i++) {
            sb.AppendLine(string.Join(" / ", words[i]).ToUpper());
        }
        wordsTMP.text = sb.ToString();

        // Award viewers points for matching with the players.
        foreach (ViewerPopupScript script in viewerPopupScripts) {
            if (script != null) {
                script.ForceDestroy();
            }
        }
        int points = GetPoints(words.Count - 1);
        for (int i = 0; i < viewerPopupScripts.Length; i++) {
            if (viewerPopupScripts[i] != null) {
                // Prevent old viewerPopupScripts from being found by ToastsScript.
                viewerPopupScripts[i].tag = "Untagged";
            }
            viewerPopupScripts[i] = Instantiate(viewerPopupPrefab, viewerPopups.transform).GetComponent<ViewerPopupScript>();
            viewerPopupScripts[i].Set(i, players.Count);
            if (i != 0) {
                // ToastsScript should only look at the first viewerpopup to see if there's room for toasts.
                viewerPopupScripts[i].tag = "Untagged";
            }
        }

        int[] matchCounts = new int[players.Count];
        Dictionary<string, int> otherSubmissions = new Dictionary<string, int>();
        HashSet<string> subscriberSubmissions = new HashSet<string>();
        lastScores = (int[])scores.Clone();
        lastDoubledUpViewers = new HashSet<string>(doubledUpViewers);
        viewerScores.FinalizeScores();
        bool anyMatches = false;
        string[] sortedViewers = viewerWords.Keys.OrderByDescending(v => subscribers.Contains(v))
                                                 .ThenByDescending(v => doubledUpViewers.Contains(v))
                                                 .ThenByDescending(v => viewerScores.GetScore(v)).ToArray();
        int viewerPopupCount = BASE_VIEWER_POPUP_COUNT - 2 * winningWords.Length + 2;
        foreach (string viewer in sortedViewers) {
            string viewerWord = viewerWords[viewer];
            bool doubledUp = doubledUpViewers.Contains(viewer);
            doubledUpViewers.Remove(viewer);
            bool matched = false;
            for (int i = 0; i < newestWords.Length; i++) {
                if (IsLemmaMatch(viewerWord, newestWords[i])) {
                    matched = true;
                    anyMatches = true;
                    scores[i] += points * (doubledUp ? 2 : 1);
                    viewerScores.Award(viewer, points * (doubledUp ? 2 : 1), time);

                    matchCounts[i]++;
                    if (matchCounts[i] <= viewerPopupCount + 1) {
                        viewerPopupScripts[i].AddLine(GetUsernameString(viewer), points, viewerScores.GetScore(viewer), doubledUp);
                    }
                }
            }
            if (matched) {
                if (viewerStreaks.ContainsKey(viewer)) {
                    viewerStreaks[viewer]++;
                    int streak = viewerStreaks[viewer];
                    if (!claimed) {
                        toastsScript.Toast(ToastType.STREAK, string.Format("{0} has matched {1} in a row!", GetUsernameString(viewer), streak == 2 ? "twice" : streak + " times"));
                    }
                }
                else {
                    viewerStreaks.Add(viewer, 1);
                }
                if (!viewersMatchedToday.Contains(viewer)) {
                    toastsScript.Toast(ToastType.FIRST_MATCH, string.Format("{0} got their first match today!", GetUsernameString(viewer)));
                    viewersMatchedToday.Add(viewer);
                }
            } else {
                if (words.Count > 1 && viewerStreaks.ContainsKey(viewer)) {
                    if (viewerStreaks[viewer] >= 3) {
                        toastsScript.Toast(ToastType.STREAK_BROKEN, string.Format("{0} broke their {1}× streak...", GetUsernameString(viewer), viewerStreaks[viewer]));
                    }
                    viewerStreaks.Remove(viewer);
                }
                if (!otherSubmissions.ContainsKey(viewerWord)) {
                    otherSubmissions.Add(viewerWord, 1);
                } else {
                    otherSubmissions[viewerWord]++;
                }
                if (subscribers.Contains(viewer)) {
                    subscriberSubmissions.Add(viewerWord);
                }
            }
        }
        for (int i = 0; i < matchCounts.Length; i++) {
            if (matchCounts[i] > viewerPopupCount + 1) {
                int over = matchCounts[i] - viewerPopupCount;
                viewerPopupScripts[i].RemoveLastLine();
                viewerPopupScripts[i].AddLine(string.Format("...and {0} {1}!", over, over == 1 ? "other" : "others"));
            }
        }
        foreach (ViewerPopupScript viewerPopupScript in viewerPopupScripts) {
            viewerPopupScript.FinalizeLines();
        }
        if (words.Count > 1 && viewerWords.Count > 0 && !anyMatches && !claimed) {
            toastsScript.Toast(ToastType.ALL_MISS, string.Format("No one matched with the hosts..."));
        }
        if (otherSubmissions.Count > 0) {
            sb.Clear();
            // Subscriber submissions.
            foreach (var kvp in otherSubmissions.OrderByDescending(kvp => kvp.Value)) {
                if (!subscriberSubmissions.Contains(kvp.Key)) {
                    continue;
                }
                if (sb.Length > 0) {
                    sb.Append(", ");
                }
                if (kvp.Value > 1) {
                    sb.Append(string.Format("{0} ({1})!", kvp.Key, kvp.Value));
                } else {
                    sb.Append(kvp.Key + "!");
                }
            }
            // Non-subscriber submissions.
            foreach (var kvp in otherSubmissions.OrderByDescending(kvp => kvp.Value)) {
                if (subscriberSubmissions.Contains(kvp.Key)) {
                    continue;
                }
                if (sb.Length > 0) {
                    sb.Append(", ");
                }
                if (kvp.Value > 1) {
                    sb.Append(string.Format("{0} ({1})", kvp.Key, kvp.Value));
                } else {
                    sb.Append(kvp.Key);
                }
            }
            botScript.WhisperAdmin("OTHER SUBMISSIONS: " + sb.ToString());
        }
        // Award the players points for matching with each other.
        if (winningWords.Length > 0) {
            gameWon = true;
            for (int i = 0; i < scores.Length; i++) {
                if (winningWords.Contains(newestWords[i])) {
                    scores[i] += points * (wordToPlayerIndex[newestWords[i]].Count - 1);
                }
            }
            confetti.Play();
            streamers.Play();
            if (lightningRound) {
                sfxLightningWin.PlayDelayed(1.25f);
            } else {
                sfxWin.PlayDelayed(1);
            }
        }
    }

    bool WordUsed(string word) {
        foreach (string[] round in words) {
            if (round.Contains(word)) {
                return true;
            }
        }
        return false;
    }
    int GetPoints(int roundNumber) {
        return ROUND_POINTS[Mathf.Min(roundNumber, ROUND_POINTS.Length - 1)];
    }
    void GrantTitle(string admin, string message) {
        string[] tokens = message.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 3) {
            botScript.Whisper(admin, "Usage: !t <username> <title>");
            return;
        }
        string username = tokens[1].ToLower();
        if (!viewerScores.SeenViewer(username)) {
            botScript.Whisper(admin, "I haven't seen a viewer with that username.");
            return;
        }
        string title = string.Join(" ", tokens, 2, tokens.Length - 2).ToUpper();
        viewerTitles[username] = title;
        toastsScript.Toast(ToastType.TITLE, GetUsernameString(username) + " was granted the title \"" + title + "\"!");
    }

    void LoadScores() {
        if (Application.isEditor) {
            return;
        }
        DirectoryInfo logDirectory = new DirectoryInfo(Application.dataPath);
        FileInfo[] logFiles = logDirectory.GetFiles("log*.txt");
        if (logFiles.Length == 0) {
            return;
        }
        Array.Sort(logFiles, (f1, f2) => f1.Name.CompareTo(f2.Name));
        FileInfo log = logFiles[logFiles.Length - 1];
        using (StreamReader sr = log.OpenText()) {
            string s = "";
            while ((s = sr.ReadLine()) != null) {
                if (s.Length == 0) {
                    continue;
                }
                string[] tokens = s.Split(':');
                viewerScores.SetTotalScore(tokens[0], int.Parse(tokens[1]));
            }
        }
        logFileSuffix = int.Parse(log.Name.Substring(3, log.Name.Length - 7)) + 1;
    }
    void SaveScores() {
        if (Application.isEditor) {
            return;
        }
        if (viewerScores.NumViewers() == 0) {
            return;
        }
        string path = Application.dataPath + "/log" + logFileSuffix + ".txt";
        List<string> output = new List<string>();
        foreach (var kvp in viewerScores.GetTotalScores().OrderByDescending(kvp => kvp.Value)) {
            output.Add(string.Format("{0}:{1}", kvp.Key, kvp.Value));
        }
        File.WriteAllLines(path, output.ToArray());
    }

    void LoadLemmas() {
        string[] lineDelimiters = new string[] { " -> " };
        char[] lemmaDelimiters = new char[] { '/' };
        char[] formDelimiters = new char[] { ',' };
        lemmaMatches = new HashSet<Tuple<string, string>>();
        foreach (string line in Regex.Split(lemmas.text, "\n|\r|\r\n")) {
            if (line.Length == 0 || line.StartsWith(";")) {
                continue;
            }
            string[] tokens = line.Split(lineDelimiters, StringSplitOptions.None);
            string lemma = tokens[0].Split(lemmaDelimiters)[0];
            string[] forms = tokens[1].Split(formDelimiters);
            // Insert every pair of lemma+form and form+form.
            for (int i = 0; i < forms.Length; i++) {
                for (int j = i + 1; j <= forms.Length; j++) {
                    string one = WORD_REGEX.Replace(forms[i], "");
                    string two = WORD_REGEX.Replace(j == forms.Length ? lemma : forms[j], "");
                    int comparison = one.CompareTo(two);
                    Debug.Assert(comparison != 0, string.Format("Identical lemma forms on line \"{0}\".", line));
                    if (comparison < 0) {
                        lemmaMatches.Add(new Tuple<string, string>(one, two));
                    } else {
                        lemmaMatches.Add(new Tuple<string, string>(two, one));
                    }
                }
            }
        }
    }
    bool IsLemmaMatch(string s1, string s2) {
        if (s1 == s2) {
            return true;
        }
        if (s1.CompareTo(s2) > 0) {
            return IsLemmaMatch(s2, s1);
        }
        return lemmaMatches.Contains(new Tuple<string, string>(s1, s2));
    }

    void LoadDictionary() {
        dictionaryDefinitions = new Dictionary<string, string>();
        //dictionaryDefinitions = JsonConvert.DeserializeObject<Dictionary<string, string>>(dictionaryAsset.text);
    }
    string GetDictionaryDefinition(string word) {
        word = word.ToUpper();
        if (dictionaryDefinitions.ContainsKey(word)) {
            return dictionaryDefinitions[word];
        }
        return null;
    }

    string JoinAwardeesGrammatically(string[] usernames) {
        for (int i = 0; i < usernames.Length; i++) {
            usernames[i] = lastDoubledUpViewers.Contains(usernames[i]) ? GetUsernameString(usernames[i]) + " (x2!)" : GetUsernameString(usernames[i]);
        }
        return Util.JoinGrammatically(usernames);
    }
    string GetUsernameString(string username) {
        return subscribers.Contains(username) ? "<color=" + GameScript.SUB_COLOR_HEX_STRING + ">" + username + "</color>" : username;
    }

    HashSet<int> GetMatchingPlayerIndices(string message) {
        message = new Regex("[^a-z ]").Replace(message.ToLower(), " ");
        HashSet<int> matchingPlayerIndices = new HashSet<int>();
        foreach (string token in message.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
            for (int i = 0; i < displayNames.Count; i++) {
                if (string.Equals(displayNames[i], token, StringComparison.OrdinalIgnoreCase)) {
                    matchingPlayerIndices.Add(i);
                }
            }
        }
        return matchingPlayerIndices;
    }
}
