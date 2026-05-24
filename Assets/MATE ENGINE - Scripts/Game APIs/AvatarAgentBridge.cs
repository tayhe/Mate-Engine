using UnityEngine;
using System;
using System.Collections;
using System.IO;

[DefaultExecutionOrder(100)]
public class AvatarAgentBridge : MonoBehaviour
{
    [Header("Toggle")]
    public bool enableAgentBridge = false;

    [Header("File Bus")]
    public float pollInterval = 0.1f;
    public float stalenessTimeout = 5.0f;

    [Header("References")]
    public Transform chatContainer;
    public Animator avatarAnimator;
    public UniversalBlendshapes blendshapes;

    [Header("Bubble")]
    public Material bubbleMaterial;
    public Sprite bubbleSprite;
    public Color bubbleColor = new Color32(120, 120, 255, 255);
    public Color fontColor = Color.white;
    public Font font;
    public int fontSize = 16;
    public int bubbleWidth = 600;
    public float textPadding = 10f;
    public float bubbleSpacing = 10f;
    [Range(5, 100)] public int streamSpeed = 35;
    [Range(1, 60)] public int bubbleDespawnTime = 10;

    [Header("Debug")]
    [SerializeField] private string lastAgentState = "disconnected";
    [SerializeField] private string lastAgentName = "";
    [SerializeField] private string lastMessage = "";

    string busPath;
    int lastSeenV = -1;
    Coroutine pollCoroutine;
    LLMUnitySamples.Bubble activeBubble;
    Coroutine streamCoroutine;
    Coroutine despawnCoroutine;
    Coroutine expressionCoroutine;

    [Serializable]
    class AgentStatus
    {
        public int v;
        public string agent;
        public string state;
        public string message;
        public float progress;
        public string error;
        public string task_name;
        public double writeUtc;
    }

    void Awake()
    {
        if (avatarAnimator == null) avatarAnimator = GetComponent<Animator>();
        if (blendshapes == null) blendshapes = GetComponent<UniversalBlendshapes>();
        busPath = Path.Combine(Application.persistentDataPath, "AgentBridge", "agent_status.json");
        try { Directory.CreateDirectory(Path.GetDirectoryName(busPath)); } catch { }
    }

    void OnEnable()
    {
        lastSeenV = -1;
        pollCoroutine = StartCoroutine(PollBus());
    }

    void OnDisable()
    {
        if (pollCoroutine != null) StopCoroutine(pollCoroutine);
        RemoveBubble();
        ResetExpressions();
    }

    IEnumerator PollBus()
    {
        var wait = new WaitForSecondsRealtime(pollInterval);
        while (true)
        {
            if (enableAgentBridge)
            {
                var status = ReadBus();
                if (status != null && status.v > lastSeenV)
                {
                    lastSeenV = status.v;
                    ProcessStatus(status);
                }
                else if (status == null && lastSeenV >= 0)
                {
                    HandleStateChange("disconnected", "", "");
                }
            }
            yield return wait;
        }
    }

    AgentStatus ReadBus()
    {
        try
        {
            if (!File.Exists(busPath)) return null;
            var s = File.ReadAllText(busPath);
            if (string.IsNullOrWhiteSpace(s)) return null;
            return JsonUtility.FromJson<AgentStatus>(s);
        }
        catch { return null; }
    }

    void ProcessStatus(AgentStatus status)
    {
        double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now - status.writeUtc > stalenessTimeout)
        {
            HandleStateChange("disconnected", "", status.agent ?? "");
            return;
        }

        string state = status.state ?? "idle";
        string message = !string.IsNullOrEmpty(status.error) ? status.error : (status.message ?? "");
        HandleStateChange(state, message, status.agent ?? "");
    }

    void HandleStateChange(string state, string message, string agentName)
    {
        if (state == lastAgentState && message == lastMessage && agentName == lastAgentName) return;
        lastAgentState = state;
        lastMessage = message;
        lastAgentName = agentName;

        if (avatarAnimator != null)
            avatarAnimator.SetBool("isTalking",
                state == "working" || state == "streaming" ||
                state == "success" || state == "error");

        ApplyExpressionForState(state);

        switch (state)
        {
            case "idle":
            case "disconnected":
                RemoveBubble();
                break;
            case "thinking":
                string thinkText = !string.IsNullOrEmpty(lastMessage) ? lastMessage : "...";
                ShowBubble(thinkText);
                break;
            case "working":
            case "streaming":
                ShowBubble(message);
                break;
            case "success":
                string successText = !string.IsNullOrEmpty(message) ? message : "Done!";
                ShowBubble(successText);
                ScheduleBubbleDespawn(5f);
                break;
            case "error":
                string errorText = !string.IsNullOrEmpty(message) ? message : "Something went wrong...";
                ShowBubble(errorText);
                ScheduleBubbleDespawn(8f);
                break;
        }
    }

    void ApplyExpressionForState(string state)
    {
        if (blendshapes == null) return;
        if (expressionCoroutine != null) StopCoroutine(expressionCoroutine);
        expressionCoroutine = StartCoroutine(FadeExpression(state));
    }

    IEnumerator FadeExpression(string state)
    {
        float tJoy = 0f, tAngry = 0f, tSorrow = 0f, tFun = 0f;
        switch (state)
        {
            case "idle": tFun = 0.3f; break;
            case "thinking": break;
            case "working": tJoy = 0.2f; break;
            case "streaming": tJoy = 0.4f; break;
            case "success": tJoy = 0.8f; tFun = 0.6f; break;
            case "error": tSorrow = 0.7f; tAngry = 0.3f; break;
            case "disconnected": break;
        }

        float elapsed = 0f;
        float duration = 0.5f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            blendshapes.Joy = Mathf.Lerp(blendshapes.Joy, tJoy, t);
            blendshapes.Angry = Mathf.Lerp(blendshapes.Angry, tAngry, t);
            blendshapes.Sorrow = Mathf.Lerp(blendshapes.Sorrow, tSorrow, t);
            blendshapes.Fun = Mathf.Lerp(blendshapes.Fun, tFun, t);
            yield return null;
        }
        blendshapes.Joy = tJoy;
        blendshapes.Angry = tAngry;
        blendshapes.Sorrow = tSorrow;
        blendshapes.Fun = tFun;
    }

    void ResetExpressions()
    {
        if (blendshapes == null) return;
        blendshapes.Joy = 0f;
        blendshapes.Angry = 0f;
        blendshapes.Sorrow = 0f;
        blendshapes.Fun = 0f;
    }

    void ShowBubble(string text)
    {
        if (chatContainer == null || string.IsNullOrEmpty(text)) return;
        RemoveBubble();

        var ui = new LLMUnitySamples.BubbleUI
        {
            sprite = bubbleSprite,
            font = font,
            fontSize = fontSize,
            fontColor = fontColor,
            bubbleColor = bubbleColor,
            bottomPosition = 0,
            leftPosition = 1,
            textPadding = textPadding,
            bubbleOffset = bubbleSpacing,
            bubbleWidth = bubbleWidth,
            bubbleHeight = -1
        };

        activeBubble = new LLMUnitySamples.Bubble(chatContainer, ui, "AgentBubble", "");
        var img = activeBubble.GetRectTransform().GetComponentInChildren<UnityEngine.UI.Image>(true);
        if (img != null && bubbleMaterial != null) img.material = bubbleMaterial;

        if (avatarAnimator != null) avatarAnimator.SetBool("isTalking", true);
        if (streamCoroutine != null) StopCoroutine(streamCoroutine);
        streamCoroutine = StartCoroutine(FakeStreamText(text));

        if (despawnCoroutine != null) StopCoroutine(despawnCoroutine);
        despawnCoroutine = StartCoroutine(DespawnAfterDelay());
    }

    IEnumerator FakeStreamText(string fullText)
    {
        if (activeBubble == null) yield break;
        activeBubble.SetText("");
        int length = 0;
        float delay = 1f / Mathf.Max(streamSpeed, 1);
        while (length < fullText.Length)
        {
            length++;
            if (activeBubble == null) yield break;
            activeBubble.SetText(fullText.Substring(0, length));
            yield return new WaitForSeconds(delay);
            if (activeBubble == null) yield break;
        }
        if (activeBubble != null) activeBubble.SetText(fullText);
        if (avatarAnimator != null) avatarAnimator.SetBool("isTalking", false);
        streamCoroutine = null;
    }

    IEnumerator DespawnAfterDelay()
    {
        float t = 0f;
        while (t < bubbleDespawnTime)
        {
            t += Time.deltaTime;
            yield return null;
        }
        RemoveBubble();
    }

    void ScheduleBubbleDespawn(float delay)
    {
        if (despawnCoroutine != null) StopCoroutine(despawnCoroutine);
        despawnCoroutine = StartCoroutine(DespawnAfterDelayCustom(delay));
    }

    IEnumerator DespawnAfterDelayCustom(float delay)
    {
        yield return new WaitForSeconds(delay);
        RemoveBubble();
    }

    void RemoveBubble()
    {
        if (streamCoroutine != null) { StopCoroutine(streamCoroutine); streamCoroutine = null; }
        if (despawnCoroutine != null) { StopCoroutine(despawnCoroutine); despawnCoroutine = null; }
        if (activeBubble != null) { activeBubble.Destroy(); activeBubble = null; }
        if (avatarAnimator != null) avatarAnimator.SetBool("isTalking", false);
    }
}
