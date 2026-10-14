using TMPro;
using UnityEngine;

/// <summary>
/// Pega este componente a cualquier TMP_Text de la escena con la CLAVE
/// de su traducción (tabla en Loc.cs): pone el texto del idioma activo
/// al activarse y se refresca solo cuando el idioma cambia.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    [SerializeField] private string key;

    private TMP_Text label;

    private void Awake() => label = GetComponent<TMP_Text>();

    private void OnEnable()
    {
        Apply();
        Loc.OnChanged += Apply;
    }

    private void OnDisable() => Loc.OnChanged -= Apply;

    private void Apply()
    {
        if (label != null && !string.IsNullOrEmpty(key))
            label.text = Loc.T(key);
    }
}
