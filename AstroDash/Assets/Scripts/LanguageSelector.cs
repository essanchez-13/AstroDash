using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Selector de idioma del panel de configuración: dos cápsulas estilo
/// segmented control (como las pestañas de la tienda) — ESPAÑOL |
/// ENGLISH. La activa lleva la cápsula gradiente cian; la otra queda
/// translúcida. Cambiar refresca TODA la UI vía Loc.OnChanged.
/// </summary>
public class LanguageSelector : MonoBehaviour
{
    [SerializeField] private Button spanishButton;
    [SerializeField] private Button englishButton;
    [SerializeField] private Sprite activePill;     // pill_btn_grad
    [SerializeField] private Sprite inactivePill;   // pill_btn_flat

    // Mismos tonos del design system que usa la tienda
    private static readonly Color Cyan = new Color(0.13f, 0.83f, 0.93f);
    private static readonly Color CyanText = new Color(0.03f, 0.04f, 0.08f);
    private static readonly Color TextDim = new Color(0.89f, 0.91f, 0.94f, 0.45f);

    private void Start()
    {
        spanishButton.onClick.AddListener(() => { Loc.Lang = "es"; Style(); });
        englishButton.onClick.AddListener(() => { Loc.Lang = "en"; Style(); });
        Style();
    }

    private void Style()
    {
        StylePill(spanishButton, Loc.Lang == "es");
        StylePill(englishButton, Loc.Lang == "en");
    }

    private void StylePill(Button btn, bool active)
    {
        btn.image.sprite = active ? activePill : inactivePill;
        btn.image.type = Image.Type.Simple;   // cápsula exacta, sin 9-slice
        btn.image.color = active ? Cyan : new Color(1f, 1f, 1f, 0.06f);
        var label = btn.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.color = active ? CyanText : TextDim;
    }
}
