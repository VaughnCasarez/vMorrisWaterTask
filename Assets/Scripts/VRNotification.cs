using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A world-space notification panel for VR. Add it to an empty GameObject (the UI is built
/// automatically), then edit the fields below or the child objects directly. It stays visible
/// in the Scene view while editing and is hidden until Show() is called in play mode.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Canvas), typeof(CanvasGroup))]
public class VRNotification : MonoBehaviour
{
    [Header("Content")]
    public string title = "Notification";
    [TextArea(2, 4)] public string message = "";
    [Tooltip("Seconds visible before fading out. 0 = stay until Hide() is called.")]
    public float displayDuration = 4f;

    [Header("Placement")]
    [Tooltip("When shown, move in front of the player's view. Disable to keep the position set in the scene.")]
    public bool placeInFrontOfCamera = true;
    public float distance = 1.5f;
    public float verticalOffset = -0.05f;
    [Tooltip("Panel size in canvas pixels. 1000 pixels = 1 meter.")]
    public Vector2 size = new Vector2(800, 300);

    [Header("Style")]
    public Color backgroundColor = new Color(0.05f, 0.07f, 0.12f, 0.88f);
    public Color accentColor = new Color(1f, 0.82f, 0.2f);
    public float accentBarHeight = 10f;
    public Color titleColor = new Color(1f, 0.82f, 0.2f);
    public float titleFontSize = 80f;
    public Color messageColor = Color.white;
    public float messageFontSize = 42f;
    public TMP_FontAsset font;

    [Header("Animation")]
    public float fadeTime = 0.35f;

    [Header("Editor")]
    [Tooltip("Keep the panel visible in edit mode so it can be previewed.")]
    public bool previewInEditor = true;

    [Header("UI References (created by 'Build UI')")]
    public Image background;
    public Image accentBar;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI messageText;

    const float WorldScale = 0.001f;

    CanvasGroup group;
    Canvas canvas;
    Coroutine running;

    void Reset()
    {
        BuildUI();
    }

    void Awake()
    {
        CacheComponents();
        if (Application.isPlaying)
        {
            SetVisible(0f);
        }
    }

    void OnEnable()
    {
        CacheComponents();
        ApplyStyle();
        if (!Application.isPlaying)
        {
            SetVisible(previewInEditor ? 1f : 0f);
        }
    }

    void OnValidate()
    {
        CacheComponents();
        ApplyStyle();
        if (!Application.isPlaying)
        {
            SetVisible(previewInEditor ? 1f : 0f);
        }
    }

    void CacheComponents()
    {
        if (canvas == null) canvas = GetComponent<Canvas>();
        if (group == null) group = GetComponent<CanvasGroup>();
    }

    /// <summary>Creates the background, accent bar and text children if they don't exist yet.</summary>
    [ContextMenu("Build UI")]
    public void BuildUI()
    {
        CacheComponents();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;
        group.interactable = false;
        group.blocksRaycasts = false;

        if (background == null)
        {
            background = CreateChild("Background").gameObject.AddComponent<Image>();
            Stretch(background.rectTransform);
        }

        if (accentBar == null)
        {
            accentBar = CreateChild("AccentBar").gameObject.AddComponent<Image>();
            var r = accentBar.rectTransform;
            r.anchorMin = new Vector2(0, 0);
            r.anchorMax = new Vector2(1, 0);
            r.pivot = new Vector2(0.5f, 0);
        }

        if (titleText == null)
        {
            titleText = CreateText("Title", new Vector2(0.05f, 0.45f), new Vector2(0.95f, 0.95f));
            titleText.fontStyle = FontStyles.Bold;
        }

        if (messageText == null)
        {
            messageText = CreateText("Message", new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.5f));
        }

        ApplyStyle();
        SetVisible(previewInEditor || Application.isPlaying ? 1f : 0f);
    }

    RectTransform CreateChild(string childName)
    {
        var go = new GameObject(childName, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        return (RectTransform)go.transform;
    }

    TextMeshProUGUI CreateText(string childName, Vector2 min, Vector2 max)
    {
        var rect = CreateChild(childName);
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = true;
        return text;
    }

    static void Stretch(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    /// <summary>Pushes the inspector values onto the UI children.</summary>
    public void ApplyStyle()
    {
        var rect = transform as RectTransform;
        if (rect != null)
        {
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * WorldScale;
        }

        if (background != null) background.color = backgroundColor;

        if (accentBar != null)
        {
            accentBar.color = accentColor;
            accentBar.rectTransform.sizeDelta = new Vector2(0, accentBarHeight);
            accentBar.rectTransform.anchoredPosition = Vector2.zero;
        }

        ApplyText(titleText, title, titleFontSize, titleColor);
        ApplyText(messageText, message, messageFontSize, messageColor);
    }

    void ApplyText(TextMeshProUGUI text, string value, float fontSize, Color color)
    {
        if (text == null) return;
        text.text = value;
        text.fontSize = fontSize;
        text.color = color;
        if (font != null) text.font = font;
    }

    void SetVisible(float alpha)
    {
        if (group == null) return;
        group.alpha = alpha;
        if (canvas != null) canvas.enabled = alpha > 0f;
    }

    /// <summary>Shows the panel using the text configured on the component.</summary>
    public void Show()
    {
        Show(title, message);
    }

    /// <summary>Shows the panel, overriding the title and message for this showing.</summary>
    public void Show(string newTitle, string newMessage)
    {
        ApplyStyle();
        ApplyText(titleText, newTitle, titleFontSize, titleColor);
        ApplyText(messageText, newMessage, messageFontSize, messageColor);

        if (placeInFrontOfCamera) PlaceInFrontOfCamera();

        if (running != null) StopCoroutine(running);
        running = StartCoroutine(Run());
    }

    public void Hide()
    {
        if (running != null) StopCoroutine(running);
        running = null;
        SetVisible(0f);
    }

    void PlaceInFrontOfCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = cam.transform.forward;

        transform.position = cam.transform.position + forward * distance + Vector3.up * verticalOffset;
        transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
    }

    IEnumerator Run()
    {
        yield return Fade(group.alpha, 1f);
        if (displayDuration <= 0f) yield break;

        yield return new WaitForSeconds(displayDuration);
        yield return Fade(1f, 0f);
        running = null;
    }

    IEnumerator Fade(float from, float to)
    {
        if (canvas != null) canvas.enabled = true;
        for (float t = 0; t < fadeTime; t += Time.deltaTime)
        {
            group.alpha = Mathf.Lerp(from, to, t / fadeTime);
            yield return null;
        }
        SetVisible(to);
    }
}
