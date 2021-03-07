using Odessa.Nets.EntityTracking;
using UnityEngine;
using static OdessaEngine.NETS.Core.NetsEntity;

namespace OdessaEngine.NETS.Core {
	public static class Extensions {
		public static bool IsServerOwned(this AuthorityEnum e) => e == AuthorityEnum.Server || e == AuthorityEnum.ServerSingleton;
		public static void SetVector3(this KeyPairEntity entity, string key, Vector3 value) {
			entity.SetVector3(key, new System.Numerics.Vector3(value.x, value.y, value.z));
		}
		public static void SetVector2(this KeyPairEntity entity, string key, Vector2 value) {
			entity.SetVector2(key, new System.Numerics.Vector2(value.x, value.y));
		}
		public static void SetVector4(this KeyPairEntity entity, string key, Vector4 value) {
			entity.SetVector4(key, new System.Numerics.Vector4(value.x, value.y, value.z, value.w));
		}
		public static Vector3 GetUnityVector3(this KeyPairEntity entity, string key) {
			var vec = entity.GetVector3(key);
			return new Vector3(vec.X, vec.Y, vec.Z);
		}
		public static Vector2 GetUnityVector2(this KeyPairEntity entity, string key) {
			var vec = entity.GetVector2(key);
			return new Vector2(vec.X, vec.Y);
		}
		public static Quaternion GetUnityQuaternion(this KeyPairEntity entity, string key) {
			var v4 = entity.GetVector4(key);
			return new Quaternion(v4.X, v4.Y, v4.Z, v4.W);
		}
		public static Vector2 ToUnityVector2(this System.Numerics.Vector2 v2) {
			return new Vector2(v2.X, v2.Y);
		}
		public static Vector3 ToUnityVector3(this System.Numerics.Vector3 v3) {
			return new Vector3(v3.X, v3.Y, v3.Z);
		}
		public static Quaternion ToUnityQuaternion(this System.Numerics.Vector4 v4) {
			return new Quaternion(v4.X, v4.Y, v4.Z, v4.W);
		}
		public static System.Numerics.Vector2 ToNumericsVector2(this Vector2 v2) {
			return new System.Numerics.Vector2(v2.x, v2.y);
		}
		public static System.Numerics.Vector3 ToNumericsVector3(this Vector3 v3) {
			return new System.Numerics.Vector3(v3.x, v3.y, v3.z);
		}
		public static System.Numerics.Vector4 ToNumericsVector4(this Quaternion quat) {
			return new System.Numerics.Vector4(quat.x, quat.y, quat.z, quat.w);
		}
	}
}