using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace PrimerVolante.VR
{
    /// <summary>
    /// Monitor de sensibilidad y presión de gatillos (triggers) para controles VR.
    /// Muestra la presión en tiempo real de cada gatillo individualmente o ambos en simultáneo.
    /// </summary>
    public class TriggerSensitivityMonitor : MonoBehaviour
    {
        [Header("Acciones de Input System (Opcional)")]
        [Tooltip("Acción de Input System para el Gatillo Izquierdo (0.0 a 1.0)")]
        public InputActionProperty leftTriggerAction;

        [Tooltip("Acción de Input System para el Gatillo Derecho (0.0 a 1.0)")]
        public InputActionProperty rightTriggerAction;

        [Header("Referencias de UI (Se auto-crean si están vacías)")]
        public Text leftValueText;
        public Image leftFillImage;
        public Text leftStatusText;

        public Text rightValueText;
        public Image rightFillImage;
        public Text rightStatusText;

        public Text globalStatusText;

        [Header("Configuración de Visualización")]
        [Range(0.001f, 0.1f)]
        public float pressureThreshold = 0.01f;

        public Color idleColor = new Color(0.35f, 0.40f, 0.48f, 1f);
        public Color activeLowColor = new Color(0.0f, 0.75f, 1.0f, 1f);   // Cyan brillante
        public Color activeMidColor = new Color(0.1f, 0.9f, 0.35f, 1f);   // Verde neón
        public Color activeFullColor = new Color(1.0f, 0.35f, 0.15f, 1f);  // Naranja/Rojo intenso

        // Lecturas internas en tiempo real
        private float m_LeftValue = 0f;
        private float m_RightValue = 0f;

        private InputAction m_DefaultLeftAction;
        private InputAction m_DefaultRightAction;

        public float LeftValue => m_LeftValue;
        public float RightValue => m_RightValue;

        private void OnEnable()
        {
            if (leftTriggerAction.action != null)
                leftTriggerAction.action.Enable();

            if (rightTriggerAction.action != null)
                rightTriggerAction.action.Enable();

            SetupDefaultActionsIfNeeded();
        }

        private void OnDisable()
        {
            if (leftTriggerAction.action != null)
                leftTriggerAction.action.Disable();

            if (rightTriggerAction.action != null)
                rightTriggerAction.action.Disable();

            m_DefaultLeftAction?.Disable();
            m_DefaultRightAction?.Disable();
        }

        private void Awake()
        {
            EnsureUIReferences();
            EnsureXRUIInputModule();
        }

        private void EnsureXRUIInputModule()
        {
            var eventSystem = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem != null)
            {
                var inputModule = eventSystem.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
                if (inputModule == null)
                {
                    var existingModule = eventSystem.GetComponent<UnityEngine.EventSystems.BaseInputModule>();
                    if (existingModule != null)
                    {
                        DestroyImmediate(existingModule);
                    }
                    eventSystem.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule>();
                }
            }
        }

        private void SetupDefaultActionsIfNeeded()
        {
            if (leftTriggerAction.action == null)
            {
                m_DefaultLeftAction = new InputAction("LeftTriggerDefault", InputActionType.Value, "<XRController>{LeftHand}/trigger");
                m_DefaultLeftAction.AddBinding("<XRController>{LeftHand}/activate");
                m_DefaultLeftAction.Enable();
            }

            if (rightTriggerAction.action == null)
            {
                m_DefaultRightAction = new InputAction("RightTriggerDefault", InputActionType.Value, "<XRController>{RightHand}/trigger");
                m_DefaultRightAction.AddBinding("<XRController>{RightHand}/activate");
                m_DefaultRightAction.Enable();
            }
        }

        private void Update()
        {
            m_LeftValue = ReadTriggerValue(leftTriggerAction, m_DefaultLeftAction, XRNode.LeftHand);
            m_RightValue = ReadTriggerValue(rightTriggerAction, m_DefaultRightAction, XRNode.RightHand);

            UpdateUI();
        }

        private float ReadTriggerValue(InputActionProperty property, InputAction defaultAction, XRNode handNode)
        {
            float val = 0f;

            // 1. Probar InputActionProperty configurada
            if (property.action != null && property.action.enabled)
            {
                val = property.action.ReadValue<float>();
            }

            // 2. Probar acción por defecto de Input System
            if (val <= 0.0001f && defaultAction != null && defaultAction.enabled)
            {
                val = defaultAction.ReadValue<float>();
            }

            // 3. Probar XRController de InputSystem
            if (val <= 0.0001f)
            {
                var controller = (handNode == XRNode.LeftHand) ?
                    UnityEngine.InputSystem.XR.XRController.leftHand :
                    UnityEngine.InputSystem.XR.XRController.rightHand;

                if (controller != null)
                {
                    var triggerControl = controller.GetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("trigger");
                    if (triggerControl != null)
                    {
                        val = triggerControl.ReadValue();
                    }
                }
            }

            // 4. Probar UnityEngine.XR.InputDevices (CommonUsages.trigger)
            if (val <= 0.0001f)
            {
                var device = InputDevices.GetDeviceAtXRNode(handNode);
                if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float devVal))
                {
                    val = devVal;
                }
            }

            return Mathf.Clamp01(val);
        }

        private void UpdateUI()
        {
            bool isLeftActive = m_LeftValue >= pressureThreshold;
            bool isRightActive = m_RightValue >= pressureThreshold;

            // Gatillo Izquierdo
            if (leftValueText != null)
                leftValueText.text = $"{m_LeftValue * 100f:F1}%\n<size=18>Raw: {m_LeftValue:F3}</size>";

            if (leftFillImage != null)
            {
                leftFillImage.fillAmount = m_LeftValue;
                leftFillImage.color = GetColorForPressure(m_LeftValue);
            }

            if (leftStatusText != null)
            {
                leftStatusText.text = isLeftActive ? "PRESIONADO" : "REPOSO";
                leftStatusText.color = isLeftActive ? activeMidColor : idleColor;
            }

            // Gatillo Derecho
            if (rightValueText != null)
                rightValueText.text = $"{m_RightValue * 100f:F1}%\n<size=18>Raw: {m_RightValue:F3}</size>";

            if (rightFillImage != null)
            {
                rightFillImage.fillAmount = m_RightValue;
                rightFillImage.color = GetColorForPressure(m_RightValue);
            }

            if (rightStatusText != null)
            {
                rightStatusText.text = isRightActive ? "PRESIONADO" : "REPOSO";
                rightStatusText.color = isRightActive ? activeMidColor : idleColor;
            }

            // Banner Global
            if (globalStatusText != null)
            {
                if (isLeftActive && isRightActive)
                {
                    globalStatusText.text = "¡AMBOS GATILLOS PRESIONADOS EN SIMULTÁNEO!";
                    globalStatusText.color = activeFullColor;
                }
                else if (isLeftActive)
                {
                    globalStatusText.text = "DETECTANDO: GATILLO IZQUIERDO";
                    globalStatusText.color = activeLowColor;
                }
                else if (isRightActive)
                {
                    globalStatusText.text = "DETECTANDO: GATILLO DERECHO";
                    globalStatusText.color = activeLowColor;
                }
                else
                {
                    globalStatusText.text = "ESPERANDO PRESIÓN EN GATILLOS VR...";
                    globalStatusText.color = idleColor;
                }
            }
        }

        private Color GetColorForPressure(float val)
        {
            if (val < pressureThreshold) return idleColor;
            if (val < 0.5f) return Color.Lerp(activeLowColor, activeMidColor, val * 2f);
            return Color.Lerp(activeMidColor, activeFullColor, (val - 0.5f) * 2f);
        }

        public void EnsureUIReferences()
        {
            if (leftValueText != null && rightValueText != null && leftFillImage != null && rightFillImage != null)
                return;

            CreateWorldSpaceUI();
        }

        [ContextMenu("Build World Space UI")]
        public void CreateWorldSpaceUI()
        {
            // Canvas
            Canvas canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1100f, 600f);
            canvasRect.localScale = new Vector3(0.0015f, 0.0015f, 0.0015f);

            // Ajustar posición si está en el origen
            if (transform.position == Vector3.zero)
            {
                transform.position = new Vector3(0f, 1.4f, 2.0f);
                transform.rotation = Quaternion.identity;
            }

            CanvasScaler scaler = GetComponent<CanvasScaler>();
            if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10;

            GraphicRaycaster raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster == null) gameObject.AddComponent<GraphicRaycaster>();

            var trackedRaycaster = GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
            if (trackedRaycaster == null) gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

            // Limpiar hijos antiguos creados previamente para evitar duplicados
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(transform.GetChild(i).gameObject);
            }

            // Material / Font
            Font defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // Panel Principal
            GameObject mainPanel = CreateUIObject("MainPanel", transform);
            Image mainBg = mainPanel.AddComponent<Image>();
            mainBg.color = new Color(0.08f, 0.10f, 0.14f, 0.92f);
            RectTransform mainRect = mainPanel.GetComponent<RectTransform>();
            StretchToParent(mainRect);

            // Header
            GameObject headerObj = CreateUIObject("Header", mainPanel.transform);
            RectTransform headerRect = headerObj.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 0.85f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.offsetMin = new Vector2(20f, 0f);
            headerRect.offsetMax = new Vector2(-20f, -10f);

            Text titleText = headerObj.AddComponent<Text>();
            titleText.font = defaultFont;
            titleText.fontSize = 32;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            titleText.text = "PRUEBA DE SENSIBILIDAD DE GATILLOS VR";

            // Status Global
            GameObject statusObj = CreateUIObject("GlobalStatus", mainPanel.transform);
            RectTransform statusRect = statusObj.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.1f, 0.73f);
            statusRect.anchorMax = new Vector2(0.9f, 0.84f);
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;

            globalStatusText = statusObj.AddComponent<Text>();
            globalStatusText.font = defaultFont;
            globalStatusText.fontSize = 22;
            globalStatusText.fontStyle = FontStyle.Bold;
            globalStatusText.alignment = TextAnchor.MiddleCenter;
            globalStatusText.color = idleColor;
            globalStatusText.text = "ESPERANDO PRESIÓN EN GATILLOS VR...";

            // Panel Contenedor de Gatillos
            GameObject triggersContainer = CreateUIObject("TriggersContainer", mainPanel.transform);
            RectTransform triggersRect = triggersContainer.GetComponent<RectTransform>();
            triggersRect.anchorMin = new Vector2(0.04f, 0.05f);
            triggersRect.anchorMax = new Vector2(0.96f, 0.70f);
            triggersRect.offsetMin = Vector2.zero;
            triggersRect.offsetMax = Vector2.zero;

            // Panel Izquierdo
            GameObject leftPanel = CreateTriggerCard("LeftTriggerCard", triggersContainer.transform,
                new Vector2(0f, 0f), new Vector2(0.48f, 1f),
                "GATILLO IZQUIERDO (LEFT)", defaultFont,
                out leftStatusText, out leftValueText, out leftFillImage);

            // Panel Derecho
            GameObject rightPanel = CreateTriggerCard("RightTriggerCard", triggersContainer.transform,
                new Vector2(0.52f, 0f), new Vector2(1f, 1f),
                "GATILLO DERECHO (RIGHT)", defaultFont,
                out rightStatusText, out rightValueText, out rightFillImage);
        }

        private GameObject CreateTriggerCard(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            string title, Font font, out Text statusTxt, out Text valTxt, out Image fillImg)
        {
            GameObject cardObj = CreateUIObject(name, parent);
            Image cardBg = cardObj.AddComponent<Image>();
            cardBg.color = new Color(0.14f, 0.17f, 0.23f, 0.9f);
            RectTransform cardRect = cardObj.GetComponent<RectTransform>();
            cardRect.anchorMin = anchorMin;
            cardRect.anchorMax = anchorMax;
            cardRect.offsetMin = Vector2.zero;
            cardRect.offsetMax = Vector2.zero;

            // Titulo Card
            GameObject titleObj = CreateUIObject("Title", cardObj.transform);
            RectTransform titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.05f, 0.82f);
            titleRect.anchorMax = new Vector2(0.95f, 0.95f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            Text titleTxt = titleObj.AddComponent<Text>();
            titleTxt.font = font;
            titleTxt.fontSize = 24;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.alignment = TextAnchor.MiddleCenter;
            titleTxt.color = Color.white;
            titleTxt.text = title;

            // Status Badge
            GameObject statusObj = CreateUIObject("Status", cardObj.transform);
            RectTransform statusRect = statusObj.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.1f, 0.68f);
            statusRect.anchorMax = new Vector2(0.9f, 0.80f);
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;

            statusTxt = statusObj.AddComponent<Text>();
            statusTxt.font = font;
            statusTxt.fontSize = 20;
            statusTxt.fontStyle = FontStyle.Bold;
            statusTxt.alignment = TextAnchor.MiddleCenter;
            statusTxt.color = idleColor;
            statusTxt.text = "REPOSO";

            // Value Display
            GameObject valObj = CreateUIObject("ValueDisplay", cardObj.transform);
            RectTransform valRect = valObj.GetComponent<RectTransform>();
            valRect.anchorMin = new Vector2(0.05f, 0.32f);
            valRect.anchorMax = new Vector2(0.95f, 0.65f);
            valRect.offsetMin = Vector2.zero;
            valRect.offsetMax = Vector2.zero;

            valTxt = valObj.AddComponent<Text>();
            valTxt.font = font;
            valTxt.fontSize = 54;
            valTxt.fontStyle = FontStyle.Bold;
            valTxt.alignment = TextAnchor.MiddleCenter;
            valTxt.color = Color.white;
            valTxt.supportRichText = true;
            valTxt.text = "0.0%\n<size=18>Raw: 0.000</size>";

            // Bar Container (Background)
            GameObject barBgObj = CreateUIObject("ProgressBarBg", cardObj.transform);
            Image barBgImg = barBgObj.AddComponent<Image>();
            barBgImg.color = new Color(0.22f, 0.26f, 0.33f, 1f);
            RectTransform barBgRect = barBgObj.GetComponent<RectTransform>();
            barBgRect.anchorMin = new Vector2(0.08f, 0.08f);
            barBgRect.anchorMax = new Vector2(0.92f, 0.28f);
            barBgRect.offsetMin = Vector2.zero;
            barBgRect.offsetMax = Vector2.zero;

            // Bar Fill (Image filled)
            GameObject fillObj = CreateUIObject("ProgressBarFill", barBgObj.transform);
            fillImg = fillObj.AddComponent<Image>();
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.fillOrigin = 0;
            fillImg.fillAmount = 0f;
            fillImg.color = activeLowColor;
            RectTransform fillRect = fillObj.GetComponent<RectTransform>();
            StretchToParent(fillRect);

            return cardObj;
        }

        private GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            return obj;
        }

        private void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
