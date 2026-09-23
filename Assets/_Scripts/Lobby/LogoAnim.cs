using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Anime l'image en ne touchant QUE localScale. Le parent (Panel) utilise un
/// VerticalLayoutGroup qui pilote anchoredPosition/sizeDelta de ses enfants à
/// chaque frame ; toucher anchoredPosition ici entrait en conflit avec lui et
/// figeait l'image sur une position au rabais captée avant le premier passage
/// du layout. localScale n'est jamais touché par un LayoutGroup, donc c'est
/// sûr de l'animer ici sans rien casser.
/// </summary>
public class LogoAnim : MonoBehaviour
{
    [Tooltip("Image UI à animer. Laisser vide pour la chercher automatiquement sur ce GameObject.")]
    public Image image;

    [Tooltip("Amplitude de l'agrandissement/rétrécissement (0.05 = ±5%).")]
    public float scaleAmount = 0.05f;

    [Tooltip("Vitesse de l'animation.")]
    public float speed = 1.5f;

    private RectTransform rectTransform;
    private Vector3 baseScale;
    private float startTime;

    void Start()
    {
        if (image == null) image = GetComponent<Image>();
        rectTransform = image != null ? image.rectTransform : GetComponent<RectTransform>();

        if (rectTransform == null) return;

        baseScale = rectTransform.localScale;
        startTime = Time.time;
    }

    void Update()
    {
        if (rectTransform == null) return;

        // Temps écoulé depuis Start(), pas Time.time global : garantit que
        // l'animation démarre pile à l'échelle actuelle (sin(0) = 0) au lieu
        // de sauter à un point aléatoire du cycle.
        float t = (Time.time - startTime) * speed;

        rectTransform.localScale = baseScale * (1f + Mathf.Sin(t) * scaleAmount);
    }
}
