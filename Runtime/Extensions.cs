using Odessa.Nets.EntityTracking;
using Odessa.Nets.EntityTracking.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEngine.SceneManagement;
using UnityEditor.Experimental.SceneManagement;
using UnityEditor;
#endif

#pragma warning disable CS0162 // Unreachable code detected
namespace OdessaEngine.NETS.Core {
	public static class Extensions {
		public static bool IsServerOwned(this AuthorityEnum e) => e == AuthorityEnum.Server || e == AuthorityEnum.ServerSingleton;

		public static void SetObject(this DictionaryModel dict, string key, object value) {
			if (value is byte b) dict.SetNumber(key, b);
			else if (value is bool bo) dict.SetNumber(key, bo ? 1 : 0);
			else if (value is short s) dict.SetNumber(key, s);
			else if (value is int i) dict.SetNumber(key, i);
			else if (value is long l) dict.SetNumber(key, l);
			else if (value is float f) dict.SetNumber(key, (long)(f * 100));
			else if (value is string st) dict.SetString(key, st);
			else if (value is Vector2 v2) dict.SetVector2(key, v2);
			else if (value is Vector3 v3) dict.SetVector3(key, v3);
			else if (value is Vector4 v4) dict.SetVector4(key, v4);
			else if (value is Quaternion q) dict.SetQuaternion(key, q);
			else throw new Exception("Unknown type " + value.GetType());
		}

		public static void SetVectorX(this DictionaryModel dict, string key, params float[] values) {
			var list = dict.GetList(key);
			for (var i = 0; i < values.Length; i++)
				list.SetNumber(i, (long)(values[i] * 100f));
		}

		public static void SetVector2(this DictionaryModel dict, string key, Vector2 value) =>
			SetVectorX(dict, key, value.x, value.y);
		public static void SetVector3(this DictionaryModel dict, string key, Vector3 value) =>
			SetVectorX(dict, key, value.x, value.y, value.z);
		public static void SetVector4(this DictionaryModel dict, string key, Vector4 value) =>
			SetVectorX(dict, key, value.x, value.y, value.z, value.w);
		public static void SetQuaternion(this DictionaryModel dict, string key, Quaternion value) =>
			SetVectorX(dict, key, value.x, value.y, value.z, value.w);

		public static object GetObject(this DictionaryModel dict, string key, Type type) {
			if (type == typeof(bool)) return dict.GetNumber(key) == 1;
			if (type == typeof(byte)) return (byte)dict.GetNumber(key);
			if (type == typeof(short)) return (short)dict.GetNumber(key);
			if (type == typeof(int)) return (int)dict.GetNumber(key);
			if (type == typeof(long)) return (long)dict.GetNumber(key);
			if (type == typeof(float)) return (float)dict.GetNumber(key) * 100f;
			if (type == typeof(string)) return dict.GetString(key);
			if (type == typeof(Vector2)) return dict.GetVector2(key);
			if (type == typeof(Vector3)) return dict.GetVector3(key);
			if (type == typeof(Vector4)) return dict.GetVector4(key);
			if (type == typeof(Quaternion)) return dict.GetQuaternion(key);
			throw new Exception("Unknown Type " + type);
		}

		public static Vector2 GetVector2(this DictionaryModel dict, string key) {
			var list = dict.GetList(key);
			return new Vector2(list.GetNumber(0).Value, list.GetNumber(1).Value);
		}
		public static Vector3 GetVector3(this DictionaryModel dict, string key) {
			var list = dict.GetList(key);
			return new Vector3(list.GetNumber(0).Value, list.GetNumber(1).Value, list.GetNumber(2).Value);
		}
		public static Vector4 GetVector4(this DictionaryModel dict, string key) {
			var list = dict.GetList(key);
			return new Vector4(list.GetNumber(0).Value, list.GetNumber(1).Value, list.GetNumber(2).Value, list.GetNumber(3).Value);
		}
		public static Quaternion GetQuaternion(this DictionaryModel dict, string key) {
			var list = dict.GetList(key);
			return new Quaternion(list.GetNumber(0).Value, list.GetNumber(1).Value, list.GetNumber(2).Value, list.GetNumber(3).Value);
		}

		public static void SetVectorX(this ListModel list, int index, params float[] values) {
			list = list.GetList(index);
			for (var i = 0; i < values.Length; i++)
				list.SetNumber(i, (long)(values[i] * 100f));
		}

		public static void SetVector2(this ListModel list, int index, Vector2 value) =>
			SetVectorX(list, index, value.x, value.y);
		public static void SetVector3(this ListModel list, int index, Vector3 value) =>
			SetVectorX(list, index, value.x, value.y, value.z);
		public static void SetVector4(this ListModel list, int index, Vector4 value) =>
			SetVectorX(list, index, value.x, value.y, value.z, value.w);
		public static void SetQuaternion(this ListModel list, int index, Quaternion value) =>
			SetVectorX(list, index, value.x, value.y, value.z, value.w);

		public static Vector2 GetVector2(this ListModel list, int index) {
			list = list.GetList(index);
			return new Vector2(list.GetNumber(0).Value, list.GetNumber(1).Value);
		}
		public static Vector3 GetVector3(this ListModel list, int index) {
			list = list.GetList(index);
			return new Vector3(list.GetNumber(0).Value, list.GetNumber(1).Value, list.GetNumber(2).Value);
		}
		public static Quaternion GetQuaternion(this ListModel list, int index) {
			list = list.GetList(index);
			return new Quaternion(list.GetNumber(0).Value, list.GetNumber(1).Value, list.GetNumber(2).Value, list.GetNumber(3).Value);
		}

		public static readonly HashSet<Type> SyncableTypes = new HashSet<Type>(){
			typeof(string),
			typeof(bool),
			typeof(byte),
			typeof(short),
			typeof(int),
			typeof(long),
			typeof(float),
			typeof(Vector2),
			typeof(Vector3),
			typeof(Vector4),
			typeof(Quaternion),
		};

		public static bool IsNetsNativeType(this Type t) => SyncableTypes.Contains(t);

		public static PropertyInfo[] GetValidNetsProperties(this Type t, bool isTopLevel) => t
			.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
			.Where(p => p.GetAccessors().Length == 2)
			.Where(p => !p.GetGetMethod().IsStatic)

			.Where(p => t != typeof(Transform) || new string[] {
				isTopLevel ? nameof(Transform.position) : nameof(Transform.localPosition),
				isTopLevel ? nameof(Transform.rotation) : nameof(Transform.localRotation),
				nameof(Transform.localScale)
			}.Contains(p.Name))

			.Where(p => t != typeof(Rigidbody2D) || new string[] {
				nameof(Rigidbody2D.velocity),
				nameof(Rigidbody2D.angularVelocity),
				nameof(Rigidbody2D.mass),
                //nameof(Rigidbody2D.drag), // linearDrag in webGL :C
                nameof(Rigidbody2D.angularDrag)
			}.Contains(p.Name))

			.Where(p => t != typeof(SpriteRenderer) || new string[] {
				nameof(SpriteRenderer.color),
				nameof(SpriteRenderer.size),
				nameof(SpriteRenderer.flipY),
				nameof(SpriteRenderer.flipX),
			}.Contains(p.Name))

			/* Checking to ensure we had a conversion type for it
             * .Where(p => TypedField.SyncableTypeLookup.ContainsKey(p.PropertyType) || new []{ typeof(Vector2), typeof(Vector3), typeof(Quaternion) }.Contains(p.PropertyType))*/
			.ToArray();

		public static bool IsPrefab(this GameObject obj) {
#if UNITY_EDITOR
			return !(PrefabUtility.GetPrefabAssetType(obj) == PrefabAssetType.MissingAsset || PrefabUtility.GetPrefabAssetType(obj) == PrefabAssetType.NotAPrefab);
#endif
			return false;
		}

		public static bool IsInPrefabMode(this GameObject obj) {
#if UNITY_EDITOR
			return PrefabStageUtility.GetCurrentPrefabStage()?.scene == SceneManager.GetActiveScene();
#endif
			return false;
		}

		public static bool IsInPrefabInstanceContext(this GameObject obj) {
#if UNITY_EDITOR
			var mode = PrefabStageUtility.GetPrefabStage(obj)?.mode;
			return mode == PrefabStage.Mode.InContext;
#endif
			return false;
		}

		public static bool IsInPrefabIsolationContext(this GameObject obj) {
#if UNITY_EDITOR
			return PrefabStageUtility.GetPrefabStage(obj)?.mode == PrefabStage.Mode.InIsolation;
#endif
			return false;
		}


	}
#pragma warning restore CS0162 // Unreachable code detected
}