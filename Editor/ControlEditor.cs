using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.ScriptableObjects;
using ControlType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType;
using ValueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType;

namespace AwAVR.VRCSDKPlus {
    /// <summary>Edits the control selected in the menu inspector. Rebuilt when the selection or control type changes.</summary>
    internal sealed class ControlEditor : VisualElement {
        private const float IconSize = 96;
        private const float CompactIconSize = 60;

        private readonly SerializedProperty _control;
        private readonly VRCExpressionsMenu _menu;
        private readonly Func<VRCExpressionParameters> _getParameters;
        private readonly bool _compact;
        private readonly List<Action> _refreshers = new List<Action>();

        internal ControlType BuiltType { get; }

        internal ControlEditor(SerializedProperty control, VRCExpressionsMenu menu,
            Func<VRCExpressionParameters> getParameters, bool compact) {
            _control = control;
            _menu = menu;
            _getParameters = getParameters;
            _compact = compact;
            BuiltType = control.FindPropertyRelative("type").ToControlType();

            EnsureArraySizes();

            Add(compact ? CreateCompactMain() : CreateMain());
            if (!compact) Add(Section(null, CreateMainParameter(true)));

            switch (BuiltType) {
                case ControlType.RadialPuppet:
                    Add(Section(null, SubParameter("Rotation", 0)));
                    break;
                case ControlType.SubMenu:
                    Add(Section(null, CreateSubMenuField()));
                    break;
                case ControlType.TwoAxisPuppet:
                    Add(Section("Axis Parameters", SubParameter("Horizontal", 0), SubParameter("Vertical", 1)));
                    Add(CreateAxisCustomization());
                    break;
                case ControlType.FourAxisPuppet:
                    Add(Section("Axis Parameters", SubParameter("Up", 0), SubParameter("Right", 1),
                        SubParameter("Down", 2), SubParameter("Left", 3)));
                    Add(CreateAxisCustomization());
                    break;
            }

            Refresh();
        }

        /// <summary>Updates the parts that aren't bound directly (warnings, style toggles, icon preview).</summary>
        internal void Refresh() {
            foreach (var refresh in _refreshers) refresh();
        }

        /// <summary>Makes sure the sub parameter and label arrays are big enough for the control type.</summary>
        private void EnsureArraySizes() {
            int subParameterCount;
            // Only the types whose UI reads sub parameters, so just selecting a control doesn't dirty the menu.
            switch (BuiltType) {
                case ControlType.RadialPuppet:
                    subParameterCount = 1;
                    break;
                case ControlType.TwoAxisPuppet:
                    subParameterCount = 2;
                    break;
                case ControlType.FourAxisPuppet:
                    subParameterCount = 4;
                    break;
                default:
                    subParameterCount = 0;
                    break;
            }

            bool changed = false;
            var subParameters = _control.FindPropertyRelative("subParameters");
            if (subParameters.arraySize < subParameterCount) {
                subParameters.arraySize = subParameterCount;
                changed = true;
            }

            if (BuiltType == ControlType.TwoAxisPuppet || BuiltType == ControlType.FourAxisPuppet) {
                var labels = _control.FindPropertyRelative("labels");
                if (labels.arraySize != 4) {
                    labels.arraySize = 4;
                    changed = true;
                }
            }

            if (changed) _control.serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        #region Main

        private VisualElement CreateMain() {
            var section = Section(null);
            var main = new VisualElement();
            main.AddToClassList("sdkp-control-main");
            section.Add(main);

            var fields = new VisualElement();
            fields.AddToClassList("sdkp-control-fields");
            fields.Add(LabeledRow("Name", CreateNameField()));
            fields.Add(LabeledRow("Type", CreateTypeField()));
            fields.Add(LabeledRow("Style", CreateStyleField()));
            main.Add(fields);
            main.Add(CreateIconField(IconSize));

            var help = new HelpBox(GetHelpMessage(BuiltType), HelpBoxMessageType.Info);
            help.style.marginTop = 3;
            section.Add(help);
            return section;
        }

        private VisualElement CreateCompactMain() {
            var section = Section(null);
            var main = new VisualElement();
            main.AddToClassList("sdkp-control-main");
            section.Add(main);

            var fields = new VisualElement();
            fields.AddToClassList("sdkp-control-fields");

            var top = Row();
            var name = CreateNameField();
            name.AddToClassList("sdkp-grow");
            top.Add(name);
            var type = CreateTypeField();
            type.AddToClassList("sdkp-grow");
            top.Add(type);
            top.Add(Toolbox.Icon(Toolbox.Strings.IconHelp, GetHelpMessage(BuiltType)));
            fields.Add(top);
            fields.Add(CreateMainParameter(false));
            fields.Add(CreateStyleField());

            main.Add(fields);
            main.Add(CreateIconField(CompactIconSize));
            return section;
        }

        private VisualElement CreateMainParameter(bool withLabel) {
            bool required = BuiltType == ControlType.Button || BuiltType == ControlType.Toggle;
            var selector = new ParameterSelector(withLabel ? "Parameter" : string.Empty,
                _control.FindPropertyRelative("parameter"), _getParameters, null, required,
                _control.FindPropertyRelative("value"));
            _refreshers.Add(selector.Refresh);
            return selector;
        }

        private TextField CreateNameField() {
            var field = new TextField();
            field.style.marginLeft = 0;
            field.AddToClassList("sdkp-grow");
            field.BindProperty(_control.FindPropertyRelative("name"));
            var placeholder = Toolbox.AddHint(field, "Name", false);
            void Update() => placeholder.SetDisplayed(string.IsNullOrEmpty(field.value));
            field.RegisterValueChangedCallback(_ => Update());
            _refreshers.Add(Update);
            return field;
        }

        private EnumField CreateTypeField() {
            var field = new EnumField(BuiltType);
            field.style.marginLeft = 0;
            field.AddToClassList("sdkp-grow");
            field.RegisterValueChangedCallback(evt => {
                evt.StopPropagation();
                if (!(evt.newValue is ControlType newType) || newType == BuiltType) return;
                ConvertType(_control, BuiltType, newType);
                _control.serializedObject.ApplyModifiedProperties();
                // The menu inspector rebuilds this editor for the new type.
            });
            return field;
        }

        private VisualElement CreateStyleField() {
            var row = Row();
            var nameProperty = _control.FindPropertyRelative("name");

            var color = new ColorField { showAlpha = false, tooltip = "Text color" };
            color.style.marginLeft = 0;
            color.AddToClassList("sdkp-grow");
            color.RegisterValueChangedCallback(evt => {
                var rawName = Regex.Replace(nameProperty.stringValue, @"</?color=?.*?>", string.Empty);
                nameProperty.stringValue = $"<color=#{ColorUtility.ToHtmlStringRGB(evt.newValue)}>{rawName}</color>";
                nameProperty.serializedObject.ApplyModifiedProperties();
            });
            row.Add(color);

            var clearColor = Toolbox.IconButton(Toolbox.Strings.IconClear, "Remove text color", () => {
                nameProperty.stringValue = Regex.Replace(nameProperty.stringValue, @"</?color=?.*?>", string.Empty);
                nameProperty.serializedObject.ApplyModifiedProperties();
            });
            row.Add(clearColor);

            ToolbarToggle LetterToggle(string text, string tooltip, char tag) {
                var toggle = new ToolbarToggle { text = text, tooltip = tooltip };
                toggle.AddToClassList("sdkp-style-toggle");
                toggle.RegisterValueChangedCallback(evt => {
                    var rawName = nameProperty.stringValue;
                    nameProperty.stringValue = evt.newValue
                        ? $"<{tag}>{rawName}</{tag}>"
                        : Regex.Replace(rawName, $@"</?{tag}>", string.Empty);
                    nameProperty.serializedObject.ApplyModifiedProperties();
                });
                row.Add(toggle);
                return toggle;
            }

            var bold = LetterToggle("<b>B</b>", "Bold", 'b');
            var italic = LetterToggle("<i>I</i>", "Italic", 'i');

            _refreshers.Add(() => {
                string rawName = nameProperty.stringValue ?? string.Empty;
                bold.SetValueWithoutNotify(rawName.Contains("<b>") && rawName.Contains("</b>"));
                italic.SetValueWithoutNotify(rawName.Contains("<i>") && rawName.Contains("</i>"));
                var textColor = Color.white;
                var m = Regex.Match(rawName, @"<color=(#[0-9A-Fa-f]{6,8})>");
                bool hasColor = m.Success && rawName.Contains("</color>") &&
                                ColorUtility.TryParseHtmlString(m.Groups[1].Value, out textColor);
                color.SetValueWithoutNotify(hasColor ? textColor : Color.white);
                clearColor.SetEnabled(hasColor);
            });

            return row;
        }

        private VisualElement CreateIconField(float size) {
            var iconProperty = _control.FindPropertyRelative("icon");
            return CreateTextureField(iconProperty, size, size, "Icon");
        }

        /// <summary>A texture preview (drop a texture on it) with an object field under it.</summary>
        private VisualElement CreateTextureField(SerializedProperty property, float width, float previewHeight,
            string emptyText) {
            var container = new VisualElement();
            container.AddToClassList("sdkp-icon-field");
            container.style.width = width;

            var preview = new Label { tooltip = "Drop a texture here" };
            preview.AddToClassList("sdkp-icon-preview");
            preview.style.width = width;
            preview.style.height = previewHeight;
            container.Add(preview);

            var field = new ObjectField {
                objectType = typeof(Texture2D),
                allowSceneObjects = false
            };
            field.style.marginLeft = 0;
            field.style.marginRight = 0;
            field.BindProperty(property);
            container.Add(field);

            void Update() {
                var texture = property.objectReferenceValue as Texture2D;
                preview.style.backgroundImage = texture ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.None);
                preview.text = texture ? string.Empty : emptyText;
                preview.EnableInClassList("sdkp-icon-preview--empty", !texture);
            }

            field.RegisterValueChangedCallback(_ => Update());
            _refreshers.Add(Update);

            preview.RegisterCallback<DragUpdatedEvent>(_ => {
                if (DragAndDrop.objectReferences.OfType<Texture2D>().Any())
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            });
            preview.RegisterCallback<DragPerformEvent>(_ => {
                var texture = DragAndDrop.objectReferences.OfType<Texture2D>().FirstOrDefault();
                if (!texture) return;
                DragAndDrop.AcceptDrag();
                property.objectReferenceValue = texture;
                property.serializedObject.ApplyModifiedProperties();
                Update();
            });
            preview.RegisterCallback<ClickEvent>(evt => {
                if (evt.clickCount == 2 && property.objectReferenceValue)
                    EditorGUIUtility.PingObject(property.objectReferenceValue);
            });

            return container;
        }

        #endregion

        #region Type specific

        private VisualElement SubParameter(string label, int index) {
            var selector = new ParameterSelector(label,
                _control.FindPropertyRelative("subParameters").GetArrayElementAtIndex(index), _getParameters,
                ValueType.Float, true);
            _refreshers.Add(selector.Refresh);
            return selector;
        }

        private VisualElement CreateSubMenuField() {
            var row = Row();
            var subMenu = _control.FindPropertyRelative("subMenu");
            var nameProperty = _control.FindPropertyRelative("name");

            var field = new ObjectField("Sub Menu") {
                objectType = typeof(VRCExpressionsMenu),
                allowSceneObjects = false
            };
            field.AddToClassList("sdkp-grow");
            field.labelElement.style.minWidth = 116;
            field.style.marginLeft = 0;
            field.BindProperty(subMenu);
            row.Add(field);

            var newButton = new Button(() => {
                var path = AssetDatabase.GetAssetPath(_menu);
                if (string.IsNullOrEmpty(path)) path = $"Assets/{_menu.name}.asset";
                var parentPath = Path.GetDirectoryName(path);
                var assetName = string.IsNullOrEmpty(nameProperty.stringValue)
                    ? $"{_menu.name} SubMenu.asset"
                    : $"{Regex.Replace(nameProperty.stringValue, "<.*?>", string.Empty)} Menu.asset";
                var newMenuPath = Toolbox.ReadyAssetPath(parentPath, assetName, true);

                var newMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                if (newMenu.controls == null) newMenu.controls = new List<VRCExpressionsMenu.Control>();

                AssetDatabase.CreateAsset(newMenu, newMenuPath);
                subMenu.objectReferenceValue = newMenu;
                subMenu.serializedObject.ApplyModifiedProperties();
            }) { text = "New", tooltip = "Create a new menu asset next to this one" };
            newButton.AddToClassList("sdkp-small-button");
            row.Add(newButton);

            var warn = Toolbox.Icon(Toolbox.Strings.IconWarn, "Submenu is empty. This control has no use.");
            row.Add(warn);

            var open = Toolbox.IconButton(Toolbox.Strings.IconFolder, "Open", () => {
                if (subMenu.objectReferenceValue is VRCExpressionsMenu menu)
                    MenuHistory.Open(MenuHistory.FindHostWindow(this), menu);
            });
            row.Add(open);

            var clear = Toolbox.IconButton(Toolbox.Strings.IconClear, "Clear", () => {
                subMenu.objectReferenceValue = null;
                subMenu.serializedObject.ApplyModifiedProperties();
            });
            row.Add(clear);

            void Update() {
                bool empty = !subMenu.objectReferenceValue;
                newButton.SetDisplayed(empty);
                warn.SetDisplayed(empty);
                open.SetEnabled(!empty);
                clear.SetEnabled(!empty);
            }

            field.RegisterValueChangedCallback(_ => Update());
            _refreshers.Add(Update);
            return row;
        }

        private VisualElement CreateAxisCustomization() {
            var section = Section("Customization");
            var labels = _control.FindPropertyRelative("labels");
            string[] names = { "Up", "Right", "Down", "Left" };
            for (int i = 0; i < 4; i++)
                section.Add(CreateAxisLabel(labels.GetArrayElementAtIndex(i), names[i]));
            return section;
        }

        private VisualElement CreateAxisLabel(SerializedProperty label, string axis) {
            var row = Row();
            row.AddToClassList("sdkp-axis-label");

            var nameField = new TextField(_compact ? string.Empty : axis);
            nameField.AddToClassList("sdkp-grow");
            nameField.style.marginLeft = 0;
            if (!_compact) nameField.labelElement.style.minWidth = 116;
            nameField.BindProperty(label.FindPropertyRelative("name"));
            if (_compact) {
                var placeholder = Toolbox.AddHint(nameField, axis, false);
                void Update() => placeholder.SetDisplayed(string.IsNullOrEmpty(nameField.value));
                nameField.RegisterValueChangedCallback(_ => Update());
                _refreshers.Add(Update);
            }

            row.Add(nameField);

            var icon = label.FindPropertyRelative("icon");
            if (_compact) {
                var iconField = new ObjectField { objectType = typeof(Texture2D), allowSceneObjects = false };
                iconField.style.width = 110;
                iconField.BindProperty(icon);
                row.Add(iconField);
            }
            else {
                row.Add(CreateTextureField(icon, 58, 40, axis));
            }

            return row;
        }

        #endregion

        #region Type conversion

        private static void ConvertType(SerializedProperty property, ControlType tOld, ControlType tNew) {
            bool IsSimple(ControlType t) => t == ControlType.Button || t == ControlType.Toggle;
            bool IsPuppet(ControlType t) => t == ControlType.RadialPuppet || t == ControlType.TwoAxisPuppet ||
                                            t == ControlType.FourAxisPuppet;

            // Leaving button/toggle: those are the only types where the main parameter is required.
            if (IsSimple(tOld) && !IsSimple(tNew))
                property.FindPropertyRelative("parameter").FindPropertyRelative("name").stringValue = "";
            else if (!IsSimple(tOld) && IsSimple(tNew))
                SetupSubParameters(property, tNew);

            if (tOld == ControlType.SubMenu || tNew == ControlType.SubMenu)
                SetupSubParameters(property, tNew);

            if (IsPuppet(tOld) && IsPuppet(tNew))
                DoPuppetConversion(property, tNew);
            else if (IsPuppet(tNew))
                SetupSubParameters(property, tNew);

            property.FindPropertyRelative("type").enumValueIndex = tNew.GetEnumValueIndex();
        }

        private static void DoPuppetConversion(SerializedProperty property, ControlType tNew) {
            var subParameters = property.FindPropertyRelative("subParameters");
            var sub0 = subParameters.arraySize > 0
                ? subParameters.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue
                : string.Empty;
            var sub1 = subParameters.arraySize > 1
                ? subParameters.GetArrayElementAtIndex(1).FindPropertyRelative("name").stringValue
                : string.Empty;

            subParameters.ClearArray();
            subParameters.InsertArrayElementAtIndex(0);
            subParameters.GetArrayElementAtIndex(0).FindPropertyRelative("name").stringValue = sub0;

            switch (tNew) {
                case ControlType.TwoAxisPuppet:
                    subParameters.InsertArrayElementAtIndex(1);
                    subParameters.GetArrayElementAtIndex(1).FindPropertyRelative("name").stringValue = sub1;
                    break;

                case ControlType.FourAxisPuppet:
                    subParameters.InsertArrayElementAtIndex(1);
                    subParameters.GetArrayElementAtIndex(1).FindPropertyRelative("name").stringValue = sub1;
                    subParameters.InsertArrayElementAtIndex(2);
                    subParameters.GetArrayElementAtIndex(2).FindPropertyRelative("name").stringValue = "";
                    subParameters.InsertArrayElementAtIndex(3);
                    subParameters.GetArrayElementAtIndex(3).FindPropertyRelative("name").stringValue = "";
                    break;
            }
        }

        private static void SetupSubParameters(SerializedProperty property, ControlType type) {
            var subParameters = property.FindPropertyRelative("subParameters");
            subParameters.ClearArray();

            int count;
            switch (type) {
                case ControlType.RadialPuppet:
                case ControlType.SubMenu:
                    count = 1;
                    break;
                case ControlType.TwoAxisPuppet:
                    count = 2;
                    break;
                case ControlType.FourAxisPuppet:
                    count = 4;
                    break;
                default:
                    count = 0;
                    break;
            }

            for (int i = 0; i < count; i++) {
                subParameters.InsertArrayElementAtIndex(i);
                subParameters.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue = "";
            }
        }

        #endregion

        internal static string GetHelpMessage(ControlType type) {
            switch (type) {
                case ControlType.Button:
                    return
                        "Click or hold to activate. The button remains active for a minimum 0.2s.\nWhile active the (Parameter) is set to (Value).\nWhen inactive the (Parameter) is reset to zero.";
                case ControlType.Toggle:
                    return
                        "Click to toggle on or off.\nWhen turned on the (Parameter) is set to (Value).\nWhen turned off the (Parameter) is reset to zero.";
                case ControlType.SubMenu:
                    return
                        "Opens another expression menu.\nWhen opened the (Parameter) is set to (Value).\nWhen closed (Parameter) is reset to zero.";
                case ControlType.TwoAxisPuppet:
                    return
                        "Puppet menu that maps the joystick to two parameters (-1 to +1).\nWhen opened the (Parameter) is set to (Value).\nWhen closed (Parameter) is reset to zero.";
                case ControlType.FourAxisPuppet:
                    return
                        "Puppet menu that maps the joystick to four parameters (0 to 1).\nWhen opened the (Parameter) is set to (Value).\nWhen closed (Parameter) is reset to zero.";
                case ControlType.RadialPuppet:
                    return
                        "Puppet menu that sets a value based on joystick rotation. (0 to 1)\nWhen opened the (Parameter) is set to (Value).\nWhen closed (Parameter) is reset to zero.";
                default:
                    return "ERROR: Unable to load message - Invalid control type";
            }
        }

        #region Layout helpers

        private static VisualElement Row() {
            var row = new VisualElement();
            row.AddToClassList("sdkp-row");
            return row;
        }

        private static VisualElement LabeledRow(string label, VisualElement field) {
            var row = Row();
            var labelElement = new Label(label);
            labelElement.AddToClassList("sdkp-field-label");
            row.Add(labelElement);
            row.Add(field);
            return row;
        }

        private static VisualElement Section(string title, params VisualElement[] children) {
            var section = new VisualElement();
            section.AddToClassList("sdkp-section");
            if (!string.IsNullOrEmpty(title)) {
                var label = new Label(title);
                label.AddToClassList("sdkp-section-title");
                section.Add(label);
            }

            foreach (var child in children) section.Add(child);
            return section;
        }

        #endregion
    }
}
