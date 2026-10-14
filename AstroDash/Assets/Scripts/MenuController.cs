using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Menú principal y panel de Configuración (diseño del handoff de
/// Claude Design). Vive en el Canvas.
///
/// - JUGAR arranca la partida; el toque en pantalla no (solo el botón).
/// - Píldora de estrellas arriba a la derecha con el saldo total.
/// - CONFIGURACIÓN: TRES sliders (Volumen general / Música / Efectos)
///   con su porcentaje al lado, conectados al AudioManager en vivo y
///   guardados entre sesiones. El de efectos suena una muestra.
/// </summary>
public class MenuController : MonoBehaviour
{
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private Button playButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button backButton;

    [Header("Sliders (con etiqueta de porcentaje)")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private TextMeshProUGUI masterValueText;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private TextMeshProUGUI musicValueText;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private TextMeshProUGUI sfxValueText;
    [Tooltip("Sonido corto para previsualizar el volumen de efectos")]
    [SerializeField] private AudioClip sfxPreviewClip;

    [Header("Saldo en el menú")]
    [SerializeField] private TextMeshProUGUI menuStarsText;

    [Header("Transición de inicio")]
    [Tooltip("CanvasGroup del panel de inicio (para desvanecerlo al jugar)")]
    [SerializeField] private CanvasGroup startPanelGroup;
    [Tooltip("Icono sobre el título: muestra la NAVE EQUIPADA")]
    [SerializeField] private Image titleShipIcon;

    private float lastPreviewTime;
    private int lastShownStars = -1;

    private void Start()
    {
        playButton.onClick.AddListener(() => StartCoroutine(PlayTransition()));
        settingsButton.onClick.AddListener(() => settingsPanel.SetActive(true));
        backButton.onClick.AddListener(() => settingsPanel.SetActive(false));

        var am = AudioManager.Instance;

        Bind(masterSlider, masterValueText,
             am != null ? am.MasterVolume : 1f,
             v => AudioManager.Instance?.SetMasterVolume(v));

        Bind(musicSlider, musicValueText,
             am != null ? am.MusicVolume : 0.5f,
             v => AudioManager.Instance?.SetMusicVolume(v));

        Bind(sfxSlider, sfxValueText,
             am != null ? am.SfxVolume : 1f,
             v =>
             {
                 if (AudioManager.Instance == null) return;
                 AudioManager.Instance.SetSfxVolume(v);
                 // Muestra para OÍR el nivel elegido (máx. 4 por segundo)
                 if (Time.unscaledTime - lastPreviewTime > 0.25f)
                 {
                     lastPreviewTime = Time.unscaledTime;
                     AudioManager.Instance.PlaySfx(sfxPreviewClip);
                 }
             });

        // Si empieza la partida, cerrar el panel de configuración
        GameManager.Instance.OnStateChanged += state =>
        {
            if (state == GameState.Playing)
                settingsPanel.SetActive(false);
        };
    }

    // Conecta un slider con su callback y su etiqueta "NN%"
    private void Bind(Slider slider, TextMeshProUGUI valueText,
                      float initial, System.Action<float> onChange)
    {
        if (slider == null) return;
        slider.SetValueWithoutNotify(initial);
        if (valueText != null) valueText.text = Mathf.RoundToInt(initial * 100f) + "%";
        slider.onValueChanged.AddListener(v =>
        {
            onChange(v);
            if (valueText != null) valueText.text = Mathf.RoundToInt(v * 100f) + "%";
        });
    }

    /// Al pulsar JUGAR: el menú se desvanece deslizándose hacia abajo y
    /// entonces arranca la partida (la nave hace su propia entrada, ver
    /// PlayerController). Los valores del panel se restauran para la
    /// próxima vez que se muestre el menú.
    private System.Collections.IEnumerator PlayTransition()
    {
        if (startPanelGroup != null)
        {
            startPanelGroup.interactable = false;
            var rt = (RectTransform)startPanelGroup.transform;
            Vector2 basePos = rt.anchoredPosition;
            const float duration = 0.32f;
            // Avance mínimo por frame: la transición dura como MUCHO ~24
            // frames aunque el reloj haga cosas raras (hipo del editor,
            // batch mode, etc.) — el juego siempre arranca
            for (float t = 0f; t < duration;
                 t += Mathf.Max(Time.unscaledDeltaTime, duration / 24f))
            {
                float e = t / duration;
                startPanelGroup.alpha = 1f - e;
                rt.anchoredPosition = basePos + Vector2.down * (240f * e * e);
                yield return null;
            }
            startPanelGroup.alpha = 1f;          // restaurar para el regreso
            startPanelGroup.interactable = true;
            rt.anchoredPosition = basePos;
        }
        GameManager.Instance.StartGame();
    }

    private void Update()
    {
        // Saldo de la píldora del menú (solo cuando cambia)
        if (menuStarsText != null && menuStarsText.isActiveAndEnabled &&
            Progress.Stars != lastShownStars)
        {
            lastShownStars = Progress.Stars;
            menuStarsText.text = lastShownStars.ToString();
        }

        // El icono del título muestra la nave EQUIPADA en cada momento
        if (titleShipIcon != null && Catalog.Instance != null)
        {
            var equipped = Catalog.Instance.GetShip(Progress.EquippedShip).sprite;
            if (titleShipIcon.sprite != equipped)
                titleShipIcon.sprite = equipped;
        }
    }
}
