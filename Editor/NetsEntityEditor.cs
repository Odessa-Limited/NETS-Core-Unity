using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using UnityEngine;

namespace OdessaEngine.NETS.Core {
    [CustomEditor(typeof(NetsEntity))]
    public class NetsEntityEditor : Editor {
        void OnEnable() {
        }

        Dictionary<string, bool> folded = new Dictionary<string, bool>();

        public override void OnInspectorGUI() {
            serializedObject.Update();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(NetsEntity._assignedGuid)));
            EditorGUI.EndDisabledGroup();
            if (PrefabStageUtility.GetCurrentPrefabStage() == null) {
                EditorGUILayout.LabelField("Please modify the prefab to modify it's networking logic.");
                serializedObject.ApplyModifiedProperties();
                return;
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(NetsEntity.SyncFramesPerSecond)));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(NetsEntity.Authority)));
            var objectsToSync = serializedObject.FindProperty(nameof(NetsEntity.ObjectsToSync));

            //GUI.backgroundColor = new Color32(47, 163, 220, 255); // Odessa color
            Texture2D texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, new Color32(0, 0, 0, 40));
            texture.Apply();
            var rectStyle = new GUIStyle();
            rectStyle.normal.background = texture;
            EditorGUILayout.BeginVertical(rectStyle);

            EditorGUILayout.PropertyField(objectsToSync, false, GUILayout.Height(40));

            if (objectsToSync.isExpanded) {
                EditorGUI.indentLevel += 1;

                for (int i = 0; i < objectsToSync.arraySize; i++) {
                    var objectToSync = objectsToSync.GetArrayElementAtIndex(i);
                    var transform = objectToSync.FindPropertyRelative(nameof(ObjectToSync.Transform));

                    var path = transform.propertyPath;
                    if (folded.ContainsKey(path) == false) folded[path] = true;
                    folded[path] = EditorGUILayout.Foldout(folded[path], ((Transform)transform.objectReferenceValue).name, true);
                    if (folded[path]) {
                        EditorGUI.indentLevel += 1;
                        RenderObjectProperties(objectToSync);
                        EditorGUILayout.Space();
                        if (GUILayout.Button("Delete")) {
                            objectsToSync.DeleteArrayElementAtIndex(i);
                            i--;
                            continue;
                        }
                        EditorGUI.indentLevel -= 1;
                    }
                }
                EditorGUILayout.Space();
                RenderNewObjectTransform(objectsToSync);
            }

            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();

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
            var fields = componentProperties.FindPropertyRelative(nameof(ComponentsToSync.Fields));
            var path = fields.propertyPath;

            if (folded.ContainsKey(path) == false) folded[path] = true;
            folded[path] = EditorGUILayout.Foldout(folded[path], name, true);
            if (folded[path]) {
                EditorGUI.indentLevel += 1;
                //RenderObjectProperties(objectToSync);

                for (int i = 0; i < fields.arraySize; i++) {
                    var field = fields.GetArrayElementAtIndex(i);
                    var enabled = field.FindPropertyRelative(nameof(ScriptFieldToSync.Enabled));
                    var fieldName = field.FindPropertyRelative(nameof(ScriptFieldToSync.FieldName));
                    var pathName = field.FindPropertyRelative(nameof(ScriptFieldToSync.PathName));
                    EditorGUILayout.PropertyField(enabled, new GUIContent(fieldName.stringValue));
                    if (enabled.boolValue) {
                        var fieldType = field.FindPropertyRelative(nameof(ScriptFieldToSync.FieldType));
                        if (new[] {"Quaternion", "Vector3", /*"Vector2", "Single"*/ }.Contains(fieldType.stringValue)) {
                            var lerpType = field.FindPropertyRelative(nameof(ScriptFieldToSync.LerpType));
                            EditorGUILayout.PropertyField(lerpType, new GUIContent("Lerp Type"));
                        }
                        //EditorGUILayout.LabelField(pathName.stringValue);
                    }


                }

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
}