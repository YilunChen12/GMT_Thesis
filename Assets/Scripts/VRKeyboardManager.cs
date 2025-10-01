using UnityEngine;
using UnityEngine.UI;
using Valve.VR;
using Valve.VR.InteractionSystem;
using System.Collections.Generic;

public class VRKeyboardManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Drag all input fields that need keyboard input here")]
    public List<InputField> inputFields = new List<InputField>();
    [Tooltip("Drag the right hand controller here")]
    public Hand rightHand;

    private bool isKeyboardActive = false;
    private ulong keyboardHandle = OpenVR.k_ulOverlayHandleInvalid;
    private InputField activeInputField;

    void Start()
    {
        Debug.Log("VRKeyboardManager: Starting initialization...");

        if (rightHand == null)
        {
            Debug.LogError("Please assign the right hand controller in the inspector!");
            return;
        }
        Debug.Log($"VRKeyboardManager: Right hand assigned: {rightHand.gameObject.name}");

        // Check if we have any input fields
        if (inputFields == null)
        {
            Debug.LogError("InputFields list is null!");
            return;
        }

        if (inputFields.Count == 0)
        {
            Debug.LogWarning("No input fields assigned to VRKeyboardManager!");
            return;
        }
        Debug.Log($"VRKeyboardManager: Found {inputFields.Count} input fields");

        // Add click listeners to all input fields
        foreach (var inputField in inputFields)
        {
            if (inputField == null)
            {
                Debug.LogWarning("Found null input field in the list!");
                continue;
            }

            if (inputField.gameObject == null)
            {
                Debug.LogError($"InputField {inputField} has no gameObject!");
                continue;
            }

            Debug.Log($"VRKeyboardManager: Setting up input field: {inputField.gameObject.name}");

            // Add UIElement component if not present
            UIElement uiElement = inputField.gameObject.GetComponent<UIElement>();
            if (uiElement == null)
            {
                Debug.Log($"VRKeyboardManager: Adding UIElement to {inputField.gameObject.name}");
                uiElement = inputField.gameObject.AddComponent<UIElement>();
                if (uiElement == null)
                {
                    Debug.LogError($"Failed to add UIElement component to {inputField.gameObject.name}");
                    continue;
                }
            }

            // Add click handler
            if (uiElement != null)
            {
                if (uiElement.onHandClick == null)
                {
                    Debug.LogError($"onHandClick is null for UIElement on {inputField.gameObject.name}");
                    continue;
                }

                Debug.Log($"VRKeyboardManager: Adding click listener to {inputField.gameObject.name}");
                uiElement.onHandClick.AddListener((hand) => OnInputFieldClicked(inputField));
            }
        }
    }

    void OnInputFieldClicked(InputField clickedField)
    {
        if (clickedField == null)
        {
            Debug.LogError("Clicked field is null!");
            return;
        }

        Debug.Log($"VRKeyboardManager: Input field clicked: {clickedField.gameObject.name}");

        // Always set the active input field when clicked
        activeInputField = clickedField;
        
        if (isKeyboardActive)
        {
            // If keyboard is already active, just update the text
            UpdateKeyboardText();
        }
        else
        {
            // Show keyboard for the clicked field
            ToggleKeyboard();
        }
    }

    void Update()
    {
        // Check for trigger press to toggle keyboard
        if (SteamVR_Actions.default_InteractUI.GetStateDown(rightHand.handType))
        {
            Debug.Log("VRKeyboardManager: Trigger pressed");
            if (activeInputField != null)
            {
                ToggleKeyboard();
            }
            else
            {
                Debug.LogWarning("No active input field selected! Please click on an input field first.");
            }
        }

        // Update keyboard position if active
        if (isKeyboardActive)
        {
            UpdateKeyboardPosition();
        }
    }

    void ToggleKeyboard()
    {
        if (!isKeyboardActive)
        {
            if (activeInputField == null)
            {
                Debug.LogWarning("No active input field selected!");
                return;
            }

            Debug.Log($"VRKeyboardManager: Showing keyboard for input field: {activeInputField.gameObject.name}");

            // Show keyboard
            var overlay = OpenVR.Overlay;
            if (overlay != null)
            {
                // Create keyboard overlay
                var error = overlay.CreateOverlay("keyboard", "Keyboard", ref keyboardHandle);
                if (error != EVROverlayError.None)
                {
                    Debug.LogError("Failed to create keyboard overlay: " + overlay.GetOverlayErrorNameFromEnum(error));
                    return;
                }

                // Show keyboard
                error = overlay.ShowKeyboard(
                    (int)EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                    (int)EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine,
                    (uint)EKeyboardFlags.KeyboardFlag_ShowArrowKeys,
                    "Enter text",
                    256,
                    activeInputField.text,
                    0
                );

                if (error != EVROverlayError.None)
                {
                    Debug.LogError("Failed to show keyboard: " + overlay.GetOverlayErrorNameFromEnum(error));
                    return;
                }

                isKeyboardActive = true;
            }
        }
        else
        {
            Debug.Log("VRKeyboardManager: Hiding keyboard");
            // Hide keyboard
            var overlay = OpenVR.Overlay;
            if (overlay != null)
            {
                overlay.HideKeyboard();
                if (keyboardHandle != OpenVR.k_ulOverlayHandleInvalid)
                {
                    overlay.DestroyOverlay(keyboardHandle);
                    keyboardHandle = OpenVR.k_ulOverlayHandleInvalid;
                }
                isKeyboardActive = false;
                // Don't clear activeInputField here, keep it for next time
            }
        }
    }

    void UpdateKeyboardPosition()
    {
        var overlay = OpenVR.Overlay;
        if (overlay != null)
        {
            // Get keyboard text
            System.Text.StringBuilder text = new System.Text.StringBuilder(256);
            overlay.GetKeyboardText(text, 256);
            if (activeInputField != null)
            {
                activeInputField.text = text.ToString();
            }
        }
    }

    void UpdateKeyboardText()
    {
        var overlay = OpenVR.Overlay;
        if (overlay != null && activeInputField != null)
        {
            // Update the keyboard with the new input field's text
            overlay.ShowKeyboard(
                (int)EGamepadTextInputMode.k_EGamepadTextInputModeNormal,
                (int)EGamepadTextInputLineMode.k_EGamepadTextInputLineModeSingleLine,
                (uint)EKeyboardFlags.KeyboardFlag_ShowArrowKeys,
                "Enter text",
                256,
                activeInputField.text,
                0
            );
        }
    }

    void OnDestroy()
    {
        // Clean up keyboard
        if (isKeyboardActive)
        {
            var overlay = OpenVR.Overlay;
            if (overlay != null)
            {
                overlay.HideKeyboard();
                if (keyboardHandle != OpenVR.k_ulOverlayHandleInvalid)
                {
                    overlay.DestroyOverlay(keyboardHandle);
                }
            }
        }
    }
}