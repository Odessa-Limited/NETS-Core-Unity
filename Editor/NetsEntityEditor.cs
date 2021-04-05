
using OdessaEngine.NETS.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(NetsEntity))]
public class NetsEntityEditor : Editor {

    Dictionary<string, bool> folded = new Dictionary<string, bool>();

    Texture _OdessaNetsLogo = null;
    Texture OdessaNetsLogo { 
        get { 
            if(_OdessaNetsLogo == null)
                _OdessaNetsLogo = AssetDatabase.LoadAssetAtPath("Packages/com.odessa.nets.core/Editor/Assets/OdessaNetsLogo.png", typeof(Texture)) as Texture;
            return _OdessaNetsLogo;
        }
    }

    public override void OnInspectorGUI() {
        //LOGO
        var controlRect = EditorGUILayout.GetControlRect();
        //controlRect.yMax = 0;
        var over500 = EditorGUIUtility.currentViewWidth < 500;
        controlRect.width = over500 ? EditorGUIUtility.currentViewWidth : 500;
        controlRect.y = 0;
        controlRect.yMin = 0;
        controlRect.yMax = controlRect.width * 0.37f;
        controlRect.x = (EditorGUIUtility.currentViewWidth / 2f) - controlRect.width / 2;
        GUI.DrawTexture(controlRect, OdessaNetsLogo, ScaleMode.ScaleToFit);
        EditorGUILayout.Space(controlRect.width * 0.37f);
        //
        serializedObject.Update();
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Entity");
        EditorGUILayout.TextField(serializedObject.targetObject.name);
        EditorGUILayout.EndHorizontal();
        //EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(NetsEntity.EntityID)));
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(NetsEntity.SyncFramesPerSecond)), new GUIContent("Send Frames Per Second"));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(NetsEntity.Authority)));
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        var objectsToSync = serializedObject.FindProperty(nameof(NetsEntity.ObjectsToSync));

        //GUI.backgroundColor = new Color32(47, 163, 220, 255); // Odessa color
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, new Color32(0, 0, 0, 40));
        texture.Apply();
        var rectStyle = new GUIStyle();
        rectStyle.normal.background = texture;
        var headingStyle = new GUIStyle(); 
        EditorGUILayout.LabelField("Synced scripts and child objects", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(rectStyle);
        if (objectsToSync.isExpanded) {
            EditorGUI.indentLevel += 1;
            RenderObject(objectsToSync.GetArrayElementAtIndex(0), Selection.activeObject as GameObject);
            EditorGUILayout.Space();
            RenderNewObjectTransform(objectsToSync);
        }

        if (objectsToSync.isExpanded) {
            EditorGUI.indentLevel += 1;
            EditorGUILayout.EndVertical();
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUI.indentLevel -= 1;
    }
    void RenderObject(SerializedProperty node, GameObject obj, string path = "") {
        var transform = node.FindPropertyRelative(nameof(ObjectToSync.Transform));
        var previousPath = path;
        path = transform.propertyPath;
        if (folded.ContainsKey(path) == false) folded[path] = true;
        folded[path] = BoldFoldout(folded[path], ((Transform)transform.objectReferenceValue).name, true);
        if (folded.ContainsKey(path) == false) folded[path] = true;
        if (folded[path]) {
            EditorGUI.indentLevel += 1;
            //Components
            if (folded.ContainsKey(path + "_components") == false) folded[path + "_components"] = true;
            folded[path + "_components"] = BoldFoldout(folded[path + "_components"], "Components", true);
            if (folded[path + "_components"]) {
                EditorGUI.indentLevel += 1;
                RenderObjectProperties(node);
                EditorGUI.indentLevel -= 1;
            }
            //Children
            var objectsToSync = serializedObject.FindProperty(nameof(NetsEntity.ObjectsToSync));
            var syncObjectChildren = new Dictionary<GameObject, SerializedProperty>();
            foreach (Transform child in obj.transform) {
                foreach (SerializedProperty synced in objectsToSync.Copy()) {
                    if (synced.FindPropertyRelative("Transform").objectReferenceValue.GetInstanceID() == child.GetInstanceID() && !syncObjectChildren.ContainsKey(child.gameObject)) {
                        syncObjectChildren.Add(child.gameObject, synced);
                    }
                }
            }
            if (syncObjectChildren.Count > 0) {
                if (folded.ContainsKey(path + "_children") == false) folded[path + "_children"] = true;
                folded[path + "_children"] = BoldFoldout(folded[path + "_children"], "Children", true);
                if (folded[path + "_children"]) {
                    EditorGUI.indentLevel += 1;
                    foreach (var child in syncObjectChildren) {
                        RenderObject(child.Value, child.Key, path);
                    }
                    EditorGUI.indentLevel -= 1;
                }
            }
            EditorGUI.indentLevel -= 1;
        }
    }
    bool BoldFoldout(bool foldoutState, string content, bool toggleOnLabelClick) {
        GUIStyle style = EditorStyles.foldout;
        FontStyle previousStyle = style.fontStyle;
        style.fontStyle = FontStyle.Bold;
        var result = EditorGUILayout.Foldout(foldoutState, content, toggleOnLabelClick, style);
        style.fontStyle = previousStyle;
        return result;
    }
    static bool TickableFoldout(SerializedProperty tickableState, bool foldoutState, string content, bool toggleOnLabelClick) {
        EditorGUILayout.BeginHorizontal();
        //TODO fix up layout of the overall positioning of the tick boxes to be inline with the middle
        var style = new GUIStyle();
        style.fixedWidth = EditorGUIUtility.currentViewWidth / 10;
        EditorGUILayout.BeginVertical(style);
        var foldoutResult = EditorGUILayout.Foldout(foldoutState, content, toggleOnLabelClick);
        EditorGUILayout.EndVertical();
        EditorGUILayout.BeginVertical(style);
        EditorGUILayout.PropertyField(tickableState.FindPropertyRelative(nameof(ComponentsToSync.AllEnabled)), new GUIContent());
        EditorGUILayout.EndVertical();
        //EditorGUILayout.BeginVertical(style);
        EditorGUILayout.PropertyField(tickableState.FindPropertyRelative(nameof(ComponentsToSync.UpdateWhen)));
        //EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
        return foldoutResult;
    }
    
    public static object GetTargetObjectOfProperty(SerializedProperty prop) {
        if (prop == null) return null;

        var path = prop.propertyPath.Replace(".Array.data[", "[");
        object obj = prop.serializedObject.targetObject;
        var elements = path.Split('.');
        foreach (var element in elements) {
            if (element.Contains("[")) {
                var elementName = element.Substring(0, element.IndexOf("["));
                var index = System.Convert.ToInt32(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
                obj = GetValue_Imp(obj, elementName, index);
            } else {
                obj = GetValue_Imp(obj, element);
            }
        }
        return obj;
    }
    
    static object GetValue_Imp(object source, string name) {
        if (source == null)
            return null;
        var type = source.GetType();

        while (type != null) {
            var f = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (f != null)
                return f.GetValue(source);

            var p = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (p != null)
                return p.GetValue(source, null);

            type = type.BaseType;
        }
        return null;
    }

    static object GetValue_Imp(object source, string name, int index) {
        var enumerable = GetValue_Imp(source, name) as System.Collections.IEnumerable;
        if (enumerable == null) return null;
        var enm = enumerable.GetEnumerator();
        //while (index-- >= 0)
        //    enm.MoveNext();
        //return enm.Current;

        for (int i = 0; i <= index; i++) {
            if (!enm.MoveNext()) return null;
        }
        return enm.Current;
    }


    void RenderObjectProperties(SerializedProperty objectProperties) {
        var fields = objectProperties.FindPropertyRelative(nameof(ObjectToSync.Components));
        for (int i = 0; i < fields.arraySize; i++) {
            var field = fields.GetArrayElementAtIndex(i);
            var fieldName = field.FindPropertyRelative(nameof(ComponentsToSync.ClassName)).stringValue;

            var componentFields = field.FindPropertyRelative(nameof(ComponentsToSync.Fields));
            if (componentFields.arraySize > 0) {
                RenderComponentsProperties(field, fieldName);
                continue;
            }
        }

    }
    void RenderComponentsProperties(SerializedProperty componentProperties, string name) {
        var field = componentProperties.FindPropertyRelative(nameof(ComponentsToSync.Fields));
        var allEnabled = componentProperties.FindPropertyRelative(nameof(ComponentsToSync.AllEnabled));
        var path = field.propertyPath;

        if (folded.ContainsKey(path) == false) folded[path] = true;
        folded[path] = TickableFoldout(componentProperties, folded[path], name, true);
        if (folded[path]) {
            if (folded.ContainsKey(path + "_allEnabled") == false) folded[path + "_allEnabled"] = true;
            var allEnabledStateHasChanged = folded[path + "_allEnabled"] != allEnabled.boolValue;
            EditorGUI.indentLevel += 1;
            //RenderObjectProperties(objectToSync);
            var sameCount = 0;
            var fieldCopy = field.Copy();
            var groupsState = true;
            for (int i = 0; i < fieldCopy.arraySize; i++) {
                var innerField = fieldCopy.GetArrayElementAtIndex(i);
                var enabled = innerField.FindPropertyRelative(nameof(ScriptFieldToSync.Enabled));
                if (i == 0)
                    groupsState = enabled.boolValue;
                if (groupsState == enabled.boolValue)
                    sameCount ++;
            }
            fieldCopy = field.Copy();
            if (sameCount < fieldCopy.arraySize && allEnabledStateHasChanged == false)
                allEnabled.boolValue = false; // something isn't true in list, so all enabled should be off and it hasn't been changed by the user
            if (sameCount == fieldCopy.arraySize && allEnabledStateHasChanged == false)
                allEnabled.boolValue = groupsState; // all are the same in the list, so all Enabled should be the same state and it hasn't been changed by the user

            for (int i = 0; i < fieldCopy.arraySize; i++) {
                var innerField = fieldCopy.GetArrayElementAtIndex(i);
                var enabled = innerField.FindPropertyRelative(nameof(ScriptFieldToSync.Enabled));
				if (allEnabledStateHasChanged) {//State changed, we need to match the state
                    enabled.boolValue = allEnabled.boolValue;
				}
                var fieldName = innerField.FindPropertyRelative(nameof(ScriptFieldToSync.FieldName));
                EditorGUILayout.PropertyField(enabled, new GUIContent(fieldName.stringValue));
                if (enabled.boolValue) {
                    EditorGUI.indentLevel += 1;
                    var fieldType = innerField.FindPropertyRelative(nameof(ScriptFieldToSync.FieldType));
                    if (new[] { "Quaternion", "Vector3", /*"Vector2", "Single"*/ }.Contains(fieldType.stringValue)) {
                        var lerpType = innerField.FindPropertyRelative(nameof(ScriptFieldToSync.LerpType));
                        EditorGUILayout.PropertyField(lerpType, new GUIContent("Lerp Type"));
                    }
                    EditorGUI.indentLevel -= 1;
                }
            }
            folded[path + "_allEnabled"] = allEnabled.boolValue;//if this wasn't changed by us above, then store it's state to find when it changes
            EditorGUI.indentLevel -= 1;
        }

    }
    void RenderNewObjectTransform(SerializedProperty objectsToSync) {
        var addedTransform = serializedObject.FindProperty(nameof(NetsEntity.addedTransform));
        if (addedTransform.objectReferenceValue != null) {
            objectsToSync.InsertArrayElementAtIndex(objectsToSync.arraySize);
            var prop = objectsToSync.GetArrayElementAtIndex(objectsToSync.arraySize - 1);
            prop.FindPropertyRelative(nameof(ObjectToSync.Transform)).objectReferenceValue = addedTransform.objectReferenceValue;
        }
        addedTransform.objectReferenceValue = null;
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(NetsEntity.addedTransform)), new GUIContent("Add Another Child:"));
        EditorGUI.indentLevel -= 1;
    }

}
