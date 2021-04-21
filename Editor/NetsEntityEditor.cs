
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

    private EntitySetting GetEntitySettings(string path) {
        EntitySetting _entitySetting = NETSNetworkedTypesLists.instance.EntitySettings.Where(o => o.lookup == path).FirstOrDefault();

        if (_entitySetting == default) {
            _entitySetting = new EntitySetting() { lookup = path };
            NETSNetworkedTypesLists.instance.EntitySettings.Add(_entitySetting);
        }
        return _entitySetting;
    }
    private EntitySetting SetEntitySettings(string path, EntitySetting entitySetting) {
        return NETSNetworkedTypesLists.instance.EntitySettings[NETSNetworkedTypesLists.instance.EntitySettings.IndexOf(NETSNetworkedTypesLists.instance.EntitySettings.Where(o=> o.lookup == path).First())] = entitySetting;
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
        var path = serializedObject.FindProperty(nameof(NetsEntity.prefab)).stringValue;
        EntitySetting entitySettings = GetEntitySettings(path);
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Entity");
        EditorGUILayout.TextField(serializedObject.targetObject.name);
        EditorGUILayout.EndHorizontal();
        //EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(NetsEntity.EntityID)));
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Send Frames Per Second");
        entitySettings.SyncFramesPerSecond = EditorGUILayout.Slider(5f, 0f, 20f);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Authority");
        entitySettings.Authority = (AuthorityEnum)EditorGUILayout.EnumPopup(entitySettings.Authority);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space();

        //GUI.backgroundColor = new Color32(47, 163, 220, 255); // Odessa color
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, new Color32(0, 0, 0, 40));
        texture.Apply();
        var rectStyle = new GUIStyle();
        rectStyle.normal.background = texture;
        var headingStyle = new GUIStyle(); 
        EditorGUILayout.LabelField("Synced scripts and child objects", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(rectStyle);
        EditorGUI.indentLevel += 1;
        if (entitySettings.ObjectsToSync.Count > 0) {
            var returnedObject = RenderObject(entitySettings, entitySettings.ObjectsToSync[0], ((NetsEntity)target).gameObject);
            entitySettings = SetEntitySettings(path, returnedObject);
        }
        EditorGUILayout.Space();
        RenderNewObjectTransform(entitySettings);
        EditorGUILayout.EndVertical();
        serializedObject.ApplyModifiedProperties();
        EditorGUI.indentLevel -= 1;
        //Make sure we're updating it
        SetEntitySettings(path, entitySettings);
    }
    EntitySetting RenderObject(EntitySetting entitySettings, ObjectToSync node, GameObject obj,int index = 0, string path = "") {
        var transform = obj.transform;
        path = transform.GetPath();
        if (folded.ContainsKey(path) == false) folded[path] = true;
        folded[path] = BoldFoldout(folded[path], transform.name, true);
        if (folded.ContainsKey(path) == false) folded[path] = true;
        if (folded[path]) {
            EditorGUI.indentLevel += 1;
            //Components
            if (folded.ContainsKey(path + "_components") == false) folded[path + "_components"] = true;
            folded[path + "_components"] = BoldFoldout(folded[path + "_components"], "Components", true);
            if (folded[path + "_components"]) {
                EditorGUI.indentLevel += 1;
                var toReturn = RenderObjectProperties(node);
                entitySettings.ObjectsToSync[index] = toReturn;
                EditorGUI.indentLevel -= 1;
            }
            //Children
            var syncObjectChildren = new List<SyncObjectChild>();
            for (int j =0; j < obj.transform.childCount;j++){
                Transform child = obj.transform.GetChild(j);
                for (int i = 0; i < entitySettings.ObjectsToSync.Count; i++) {
                    ObjectToSync synced = entitySettings.ObjectsToSync[i];
                    if (synced.Transform.name == child.name && syncObjectChildren.Where(c => c.gameObject == child.gameObject).Any() == false) {
                        syncObjectChildren.Add(
                            new SyncObjectChild() {
                                gameObject = child.gameObject,
                                objectToSync = synced,
                                index = i
                            }); 
                    }
                }
            }
            if (syncObjectChildren.Count > 0) {
                if (folded.ContainsKey(path + "_children") == false) folded[path + "_children"] = true;
                folded[path + "_children"] = BoldFoldout(folded[path + "_children"], "Children", true);
                if (folded[path + "_children"]) {
                    EditorGUI.indentLevel += 1;
                    foreach (var child in syncObjectChildren) {
                        entitySettings = RenderObject(entitySettings, child.objectToSync, child.gameObject, child.index, path);
                    }
                    EditorGUI.indentLevel -= 1;
                }
            }
            EditorGUI.indentLevel -= 1;
        }
        return entitySettings;
    }
    class SyncObjectChild {
        public GameObject gameObject;
        public ObjectToSync objectToSync;
        public int index;
	}
    bool BoldFoldout(bool foldoutState, string content, bool toggleOnLabelClick) {
        GUIStyle style = EditorStyles.foldout;
        FontStyle previousStyle = style.fontStyle;
        style.fontStyle = FontStyle.Bold;
        var result = EditorGUILayout.Foldout(foldoutState, content, toggleOnLabelClick, style);
        style.fontStyle = previousStyle;
        return result;
    }
    static TickableFoldoutState TickableFoldout(bool currentState, OdessaRunWhen currentRunWhen, bool foldoutState, string content, bool toggleOnLabelClick) {
        EditorGUILayout.BeginHorizontal();
        //TODO fix up layout of the overall positioning of the tick boxes to be inline with the middle
        EditorGUILayout.BeginVertical();
        var foldoutResult = EditorGUILayout.Foldout(foldoutState, content, toggleOnLabelClick);
        EditorGUILayout.EndVertical();
        var style = new GUIStyle();
        style.padding = new RectOffset((int)(EditorGUIUtility.currentViewWidth / 8), 0, 0, 0);
        EditorGUILayout.BeginVertical(style);
        var toggleResult = EditorGUILayout.Toggle(currentState);
        EditorGUILayout.EndVertical();
        var style2 = new GUIStyle();
        EditorGUILayout.LabelField("Update when");
        OdessaRunWhen updateWhenResult = (OdessaRunWhen)EditorGUILayout.EnumPopup(currentRunWhen, GUILayout.ExpandWidth(true), GUILayout.MinWidth(100));
        EditorGUILayout.EndVertical();
        return new TickableFoldoutState() { folded = foldoutResult, toggled = toggleResult, updateWhen = updateWhenResult };
    }
    public class TickableFoldoutState {
        public bool folded = false;
        public bool toggled = false;
        public OdessaRunWhen updateWhen = OdessaRunWhen.Always;
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


    ObjectToSync RenderObjectProperties(ObjectToSync objectToSync) {
        var fields = objectToSync.Components;
        for (int i = 0; i < fields.Count; i++) {
            var field = fields[i];
            var fieldName = field.ClassName;

            var componentFields = field.Fields;
            if (componentFields.Count > 0) {
                fields[i] = RenderComponentsProperties(field, fieldName);
                continue;
            }
        }
        objectToSync.Components = fields;
        return objectToSync;
    }
    ComponentsToSync RenderComponentsProperties(ComponentsToSync componentSync, string name) {
        var fields = componentSync.Fields;
        var path = componentSync.Path;

        if (folded.ContainsKey(path) == false) folded[path] = true;
        var result = TickableFoldout(componentSync.AllEnabled, componentSync.UpdateWhen, folded[path], name, true);
        folded[path] = result.folded;
        var allEnabled = result.toggled;
        var allEnabledStateHasChanged = componentSync.AllEnabled != allEnabled;
        componentSync.UpdateWhen = result.updateWhen;
        if (folded[path]) {
            if (folded.ContainsKey(path + "_allEnabled") == false) folded[path + "_allEnabled"] = true;
            EditorGUI.indentLevel += 1;
            //RenderObjectProperties(objectToSync);
            var sameCount = 0;
            var groupsState = true;
            for (int i = 0; i < fields.Count; i++) {
                var innerField = fields[i];
                var enabled = innerField.Enabled;
                if (i == 0)
                    groupsState = enabled;
                if (groupsState == enabled)
                    sameCount ++;
            }
            if (sameCount < fields.Count && allEnabledStateHasChanged == false)
                allEnabled = false; // something isn't true in list, so all enabled should be off and it hasn't been changed by the user
            if (sameCount == fields.Count && allEnabledStateHasChanged == false) {
                allEnabled = groupsState; // all are the same in the list, so all Enabled should be the same state and it hasn't been changed by the user
                allEnabledStateHasChanged = true;
            }
            folded[path + "_allEnabled"] = allEnabled;
            var stateEnabledThisFrame = 0;
            for (int i = 0; i < fields.Count; i++) {
                var innerField = fields[i];
                var enabled = innerField.Enabled;
                if (allEnabledStateHasChanged) {//State changed, we need to match the state
                    enabled = allEnabled;
				}
                var fieldName = innerField.FieldName;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(fieldName);
                innerField.Enabled = EditorGUILayout.Toggle(enabled);
                if (enabled) {
                    stateEnabledThisFrame++;
                    EditorGUI.indentLevel += 1;
                    var fieldType = innerField.FieldType;
                    if (new[] { "Quaternion", "Vector3", /*"Vector2", "Single"*/ }.Contains(fieldType)) {

                        EditorGUILayout.BeginHorizontal();
                        innerField.LerpType = (LerpType)EditorGUILayout.EnumPopup(innerField.LerpType);
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUI.indentLevel -= 1;
                }
                EditorGUILayout.EndHorizontal();
                var newFeilds = new List<ScriptFieldToSync>();
                newFeilds.AddRange(componentSync.Fields);
                newFeilds[i] = innerField;
                componentSync.Fields = newFeilds;
            }
            componentSync.AllEnabled = allEnabled;
            EditorGUI.indentLevel -= 1;
        }
        return componentSync;
    }
    void RenderNewObjectTransform(EntitySetting settings) {
        var addedTransform = serializedObject.FindProperty(nameof(NetsEntity.addedTransform));
        if (addedTransform.objectReferenceValue != null && settings.ObjectsToSync.Where( o => o.Transform == addedTransform.objectReferenceValue as Transform).Any() == false) {
            var newSyncObj = new ObjectToSync() {
                Transform = addedTransform.objectReferenceValue as Transform,
                Components = new List<ComponentsToSync>(),
            };
            settings.ObjectsToSync.Add(newSyncObj);
        }
        addedTransform.objectReferenceValue = null;
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(NetsEntity.addedTransform)), new GUIContent("Add Another Child:"));
        EditorGUI.indentLevel -= 1;
    }

}
