using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// TIENDA + INVENTARIO con pestañas (diseño del handoff de Claude
/// Design): filas con icono en caja redondeada, nombre y botón de
/// estado a la derecha:
///   - No comprado → "precio ★" dorado (apagado si no alcanza)
///   - Comprado    → "EQUIPAR" cian
///   - Equipado    → "EQUIPADO" cian translúcido
/// El esqueleto del panel lo construye el editor (Fases 13/15); las
/// filas se generan aquí en runtime leyendo Catalog + Progress.
/// </summary>
public class ShopController : MonoBehaviour
{
    private enum Tab { Ships, Shots, Backgrounds }

    [SerializeField] private GameObject shopPanel;
    [SerializeField] private Button shopButton;       // en el menú principal
    [SerializeField] private Button closeButton;
    [SerializeField] private Button shipsTabButton;
    [SerializeField] private Button shotsTabButton;
    [SerializeField] private Button backgroundsTabButton;
    [SerializeField] private RectTransform content;   // contenedor de filas
    [SerializeField] private TextMeshProUGUI starsText;
    [SerializeField] private Sprite starIcon;         // estrella de 5 puntas
    [SerializeField] private Sprite circleIcon;       // muestra de color de fondo
    [Tooltip("Sprite LIMPIO de la bala (sin estela ni aura): solo para la UI")]
    [SerializeField] private Sprite bulletIcon;
    [Tooltip("Rectángulo redondeado 9-slice plano (filas, caja de icono)")]
    [SerializeField] private Sprite roundedSprite;
    [Tooltip("Rectángulo redondeado 9-slice con gradiente vertical")]
    [SerializeField] private Sprite gradientSprite;
    [Header("Cápsulas dedicadas (Fase 23 — píldoras sin deformación)")]
    [SerializeField] private Sprite tabPillSprite;        // pestaña activa
    [SerializeField] private Sprite buttonPillSprite;     // EQUIPAR / precio (gradiente)
    [SerializeField] private Sprite buttonPillFlatSprite; // EQUIPADO / apagado (plano)
    [Header("Tipografías del diseño")]
    [SerializeField] private TMP_FontAsset headingFont;   // Space Grotesk
    [SerializeField] private TMP_FontAsset bodyFont;      // Rubik

    // Paleta del handoff
    private static readonly Color Cyan = new Color(0.247f, 0.878f, 0.937f);      // #3FE0EF
    private static readonly Color CyanText = new Color(0.016f, 0.078f, 0.102f);  // #04141A
    private static readonly Color Gold = new Color(1f, 0.82f, 0.4f);             // #FFD166
    private static readonly Color GoldText = new Color(0.227f, 0.141f, 0f);      // #3A2400
    private static readonly Color TextLight = new Color(0.933f, 0.949f, 0.973f); // #EEF2F8
    private static readonly Color TextDim = new Color(1f, 1f, 1f, 0.45f);
    private static readonly Color RowBg = new Color(1f, 1f, 1f, 0.045f);
    private static readonly Color BoxBg = new Color(1f, 1f, 1f, 0.05f);

    private Tab currentTab = Tab.Ships;

    private void Start()
    {
        shopButton.onClick.AddListener(() => { shopPanel.SetActive(true); Render(); });
        closeButton.onClick.AddListener(() => shopPanel.SetActive(false));
        shipsTabButton.onClick.AddListener(() => { currentTab = Tab.Ships; Render(); });
        shotsTabButton.onClick.AddListener(() => { currentTab = Tab.Shots; Render(); });
        backgroundsTabButton.onClick.AddListener(() => { currentTab = Tab.Backgrounds; Render(); });

        GameManager.Instance.OnStateChanged += state =>
        {
            if (state == GameState.Playing) shopPanel.SetActive(false);
        };
    }

    // ------------------- Pintado -------------------

    private void Render()
    {
        starsText.text = Progress.Stars.ToString();

        StyleTab(shipsTabButton, currentTab == Tab.Ships);
        StyleTab(shotsTabButton, currentTab == Tab.Shots);
        StyleTab(backgroundsTabButton, currentTab == Tab.Backgrounds);

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        var cat = Catalog.Instance;
        switch (currentTab)
        {
            case Tab.Ships:
                foreach (var s in cat.ships)
                    AddRow(s.id, s.displayName, s.price, s.sprite, Color.white,
                           Progress.EquippedShip == s.id, () => EquipShip(s.id));
                break;
            case Tab.Shots:
                // Sprite limpio de la bala teñido del color del disparo
                foreach (var s in cat.shots)
                    AddRow(s.id, s.displayName, s.price, bulletIcon, s.color,
                           Progress.EquippedShot == s.id, () => Progress.EquippedShot = s.id);
                break;
            case Tab.Backgrounds:
                foreach (var b in cat.backgrounds)
                    AddRow(b.id, b.displayName, b.price, roundedSprite,
                           new Color(b.cameraColor.r + 0.06f, b.cameraColor.g + 0.06f,
                                     b.cameraColor.b + 0.10f),
                           Progress.EquippedBackground == b.id, () => EquipBackground(b.id));
                break;
        }
    }

    // Pestaña estilo "segmented control": activa = cápsula cian con
    // texto oscuro; inactiva = transparente con texto al 45%
    private void StyleTab(Button tab, bool active)
    {
        tab.image.sprite = active ? tabPillSprite : null;
        tab.image.type = Image.Type.Simple;   // cápsula exacta, sin 9-slice
        tab.image.color = active ? Cyan : Color.clear;
        var label = tab.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null) label.color = active ? CyanText : TextDim;
    }

    private void EquipShip(string id)
    {
        Progress.EquippedShip = id;
        var player = FindFirstObjectByType<PlayerController>();
        if (player != null) player.ApplyEquipment();
    }

    private void EquipBackground(string id)
    {
        Progress.EquippedBackground = id;
        Camera.main.backgroundColor = Catalog.Instance.GetBackground(id).cameraColor;
    }

    // ------------------- Construcción de filas -------------------

    private void AddRow(string id, string label, int price, Sprite icon,
                        Color iconColor, bool equipped, System.Action equip)
    {
        var row = new GameObject(id, typeof(RectTransform));
        row.transform.SetParent(content, false);
        var le = row.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = 150f;
        var rowImg = row.AddComponent<Image>();
        rowImg.color = RowBg;
        rowImg.sprite = roundedSprite;
        rowImg.type = Image.Type.Sliced;

        // Caja redondeada del icono (como el diseño: 44px → ~92px aquí)
        var box = new GameObject("IconBox", typeof(RectTransform));
        box.transform.SetParent(row.transform, false);
        var xrt = (RectTransform)box.transform;
        xrt.anchorMin = xrt.anchorMax = xrt.pivot = new Vector2(0f, 0.5f);
        xrt.anchoredPosition = new Vector2(28f, 0f);
        xrt.sizeDelta = new Vector2(105f, 105f);
        var boxImg = box.AddComponent<Image>();
        boxImg.sprite = roundedSprite;
        boxImg.type = Image.Type.Sliced;
        boxImg.color = BoxBg;

        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.transform.SetParent(box.transform, false);
        ((RectTransform)iconGo.transform).sizeDelta = new Vector2(84f, 84f);
        var img = iconGo.AddComponent<Image>();
        img.color = iconColor;
        if (icon != null)
        {
            img.sprite = icon;
            img.preserveAspect = true;
        }

        // Nombre (Rubik, como el cuerpo del diseño)
        var name = new GameObject("Name", typeof(RectTransform));
        name.transform.SetParent(row.transform, false);
        var nrt = (RectTransform)name.transform;
        nrt.anchorMin = nrt.anchorMax = nrt.pivot = new Vector2(0f, 0.5f);
        nrt.anchoredPosition = new Vector2(160f, 0f);
        nrt.sizeDelta = new Vector2(400f, 130f);
        var tmp = name.AddComponent<TextMeshProUGUI>();
        if (bodyFont != null) tmp.font = bodyFont;
        tmp.text = label;
        tmp.fontSize = 37f;
        tmp.color = TextLight;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;

        // Botón de estado (derecha)
        bool owned = Progress.IsOwned(id);
        var btnGo = new GameObject("Action", typeof(RectTransform));
        btnGo.transform.SetParent(row.transform, false);
        var brt = (RectTransform)btnGo.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 0.5f);
        brt.anchoredPosition = new Vector2(-24f, 0f);
        brt.sizeDelta = new Vector2(280f, 84f);
        var bimg = btnGo.AddComponent<Image>();
        bimg.type = Image.Type.Simple;   // cápsulas dedicadas, sin 9-slice
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = bimg;

        var lblGo = new GameObject("Label", typeof(RectTransform));
        lblGo.transform.SetParent(btnGo.transform, false);
        ((RectTransform)lblGo.transform).sizeDelta = new Vector2(280f, 84f);
        var lbl = lblGo.AddComponent<TextMeshProUGUI>();
        if (headingFont != null) lbl.font = headingFont;
        lbl.fontSize = 30f;
        lbl.alignment = TextAlignmentOptions.Center;
        lbl.fontStyle = FontStyles.Bold;

        if (equipped)
        {
            // EQUIPADO: cápsula cian translúcida con texto cian
            bimg.sprite = buttonPillFlatSprite;
            bimg.color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.15f);
            lbl.text = Loc.T("equipped");
            lbl.color = Cyan;
            btn.interactable = false;
        }
        else if (owned)
        {
            // EQUIPAR: cápsula gradiente cian con texto oscuro
            bimg.sprite = buttonPillSprite;
            bimg.color = Cyan;
            lbl.text = Loc.T("equip");
            lbl.color = CyanText;
            btn.onClick.AddListener(() => { equip(); Render(); });
        }
        else
        {
            // COMPRAR: "precio ★" dorado; apagado si el saldo no alcanza
            bool canAfford = Progress.Stars >= price;
            bimg.sprite = canAfford ? buttonPillSprite : buttonPillFlatSprite;
            bimg.color = canAfford ? Gold : new Color(1f, 1f, 1f, 0.06f);
            lbl.text = price.ToString();
            lbl.color = canAfford ? GoldText : new Color(1f, 1f, 1f, 0.35f);

            // Estrellita junto al precio
            var starGo = new GameObject("Star", typeof(RectTransform));
            starGo.transform.SetParent(btnGo.transform, false);
            var srt = (RectTransform)starGo.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
            srt.sizeDelta = new Vector2(34f, 34f);
            var starImg = starGo.AddComponent<Image>();
            starImg.sprite = starIcon;
            starImg.color = canAfford ? GoldText : new Color(1f, 1f, 1f, 0.35f);
            // Precio a la izquierda del centro, estrella a la derecha
            float shift = 26f + 10f * price.ToString().Length;
            ((RectTransform)lblGo.transform).anchoredPosition = new Vector2(-22f, 0f);
            srt.anchoredPosition = new Vector2(shift, 0f);

            btn.interactable = canAfford;
            btn.onClick.AddListener(() =>
            {
                if (Progress.TrySpend(price)) Progress.Own(id);
                Render();
            });
        }
    }
}
