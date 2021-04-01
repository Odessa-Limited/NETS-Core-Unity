
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public static class EditorExtensionMethods {
    public static GameObject GetGameObject(this SerializedProperty property) {
        return property.serializedObject.targetObject as GameObject;
    }
    public static IEnumerable<T> ToEnumerable<T>(this IEnumerator<T> enumerator) {
        while (enumerator.MoveNext())
            yield return enumerator.Current;
    }
    public static IEnumerable<T> GetEnumerator<T>(this SerializedProperty serializedProperty) {
        return serializedProperty.GetEnumerator<T>();
    }
}

