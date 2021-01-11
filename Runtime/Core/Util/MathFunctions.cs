using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OdessaEngine.NETS.Core.Util {

	public static class MathFunctions {

		public static float CopySign(float val, float sign) {
			return Mathf.Sign(sign) * Mathf.Abs(val);
		}

		public static float pi = Mathf.PI;
		public static float fullCircle = (Mathf.PI * 2);

		public static float normalize(float angle) {
			if (angle < 0)
				return fullCircle + (angle % -fullCircle);
			return angle % fullCircle;
		}

		public static float angleDifference(float targetAngle, float currentAngle) {
			targetAngle = normalize(targetAngle);
			currentAngle = normalize(currentAngle);

			if (targetAngle > currentAngle + pi)
				currentAngle += fullCircle;
			if (targetAngle < currentAngle - pi)
				currentAngle -= fullCircle;

			return targetAngle - currentAngle;
		}

		public static Vector2 Vector2FromAngle(float angle) {
			return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
		}

		public static Vector2 Rotate(this Vector2 v, float degrees) {
			float sin = Mathf.Sin(degrees * Mathf.Deg2Rad);
			float cos = Mathf.Cos(degrees * Mathf.Deg2Rad);

			float tx = v.x;
			float ty = v.y;
			v.x = (cos * tx) - (sin * ty);
			v.y = (sin * tx) + (cos * ty);
			return v;
		}

		public struct IntVector3 {
			public int x { get; set; }
			public int y { get; set; }
			public int z { get; set; }

			public IntVector3(int x, int y, int z) {
				this.x = x;
				this.y = y;
				this.z = z;
			}
			public IntVector3(Vector3 pos) {
				x = (int)pos.x;
				y = (int)pos.y;
				z = (int)pos.z;
			}

			public int this[int i] {
				get { return i == 0 ? x : (i == 1 ? y : z); }
				set {
					if (i == 0) x = value;
					else if (i == 1) y = value;
					else if (i == 2) z = value;
				}
			}


			public static IntVector3 operator +(IntVector3 v, IntVector3 o) {
				return new IntVector3 {
					x = v.x + o.x,
					y = v.y + o.y,
					z = v.z + o.z,
				};
			}
			public static IntVector3 operator +(IntVector3 v, int i) {
				return new IntVector3 {
					x = v.x + i,
					y = v.y + i,
					z = v.z + i,
				};
			}
			public static IntVector3 operator -(IntVector3 v, IntVector3 o) {
				return new IntVector3 {
					x = v.x - o.x,
					y = v.y - o.y,
					z = v.z - o.z,
				};
			}
			public static IntVector3 operator -(IntVector3 v, int i) {
				return new IntVector3 {
					x = v.x - i,
					y = v.y - i,
					z = v.z - i,
				};
			}
			public static IntVector3 operator -(IntVector3 v) {
				return new IntVector3 {
					x = -v.x,
					y = -v.y,
					z = -v.z,
				};
			}
			public static IntVector3 operator *(IntVector3 v, IntVector3 o) {
				return new IntVector3 {
					x = v.x * o.x,
					y = v.y * o.y,
					z = v.z * o.z,
				};
			}
			public static IntVector3 operator /(IntVector3 v, int i) {
				return new IntVector3 {
					x = v.x / i,
					y = v.y / i,
					z = v.z / i,
				};
			}
			public static IntVector3 operator *(IntVector3 v, int i) {
				return new IntVector3 {
					x = v.x * i,
					y = v.y * i,
					z = v.z * i,
				};
			}
			public static bool operator <(IntVector3 v, IntVector3 o) {
				return v.x < o.x && v.y < o.y && v.z < o.z;
			}
			public static bool operator >(IntVector3 v, IntVector3 o) {
				return v.x > o.x && v.y > o.y && v.z > o.z;
			}
			public static bool operator <=(IntVector3 v, IntVector3 o) {
				return v.x <= o.x && v.y <= o.y && v.z <= o.z;
			}
			public static bool operator >=(IntVector3 v, IntVector3 o) {
				return v.x >= o.x && v.y >= o.y && v.z >= o.z;
			}
			public IntVector3 abs() {
				return new IntVector3(
					x < 0 ? -x : x,
					y < 0 ? -y : y,
					z < 0 ? -z : z
				);
			}

			public void Clear() {
				x = y = z = 0;
			}

			public Vector3 ToVector3() {
				return new Vector3(x, y, z);
			}

			public override string ToString() {
				return "(" + x + ", " + y + ", " + x + ")";
			}

			public float Magnitude {
				get { return Mathf.Sqrt(MagnitudeSq); }
			}
			public float MagnitudeSq { get { return (x * x) + (y * y); } }

		}

		public struct IntVector2 {
			public int x { get; set; }
			public int y { get; set; }

			public IntVector2(int x, int y) {
				this.x = x;
				this.y = y;
			}

			public int this[int i] {
				get { return i == 0 ? x : y; }
				set {
					if (i == 0) x = value;
					else y = value;
				}
			}


			public static IntVector2 operator +(IntVector2 v, IntVector2 o) {
				return new IntVector2 {
					x = v.x + o.x,
					y = v.y + o.y,
				};
			}
			public static IntVector2 operator +(IntVector2 v, int i) {
				return new IntVector2 {
					x = v.x + i,
					y = v.y + i,
				};
			}
			public static IntVector2 operator -(IntVector2 v, IntVector2 o) {
				return new IntVector2 {
					x = v.x - o.x,
					y = v.y - o.y,
				};
			}
			public static IntVector2 operator -(IntVector2 v, int i) {
				return new IntVector2 {
					x = v.x - i,
					y = v.y - i,
				};
			}
			public static IntVector2 operator -(IntVector2 v) {
				return new IntVector2 {
					x = -v.x,
					y = -v.y,
				};
			}
			public static IntVector2 operator *(IntVector2 v, IntVector2 o) {
				return new IntVector2 {
					x = v.x * o.x,
					y = v.y * o.y,
				};
			}
			public static IntVector2 operator /(IntVector2 v, int i) {
				return new IntVector2 {
					x = v.x / i,
					y = v.y / i,
				};
			}
			public static IntVector2 operator *(IntVector2 v, int i) {
				return new IntVector2 {
					x = v.x * i,
					y = v.y * i,
				};
			}
			public static bool operator <(IntVector2 v, IntVector2 o) {
				return v.x < o.x && v.y < o.y;
			}
			public static bool operator >(IntVector2 v, IntVector2 o) {
				return v.x > o.x && v.y > o.y;
			}
			public static bool operator <=(IntVector2 v, IntVector2 o) {
				return v.x <= o.x && v.y <= o.y;
			}
			public static bool operator >=(IntVector2 v, IntVector2 o) {
				return v.x >= o.x && v.y >= o.y;
			}
			public IntVector2 abs() {
				return new IntVector2(
					x < 0 ? -x : x,
					y < 0 ? -y : y
				);
			}

			public void Clear() {
				x = y = 0;
			}

			public Vector3 ToVector3() {
				return new Vector3(x, 0, y);
			}

			public override string ToString() {
				return "(" + x + ", " + y + ")";
			}

			public float Magnitude { get { return Mathf.Sqrt(MagnitudeSq); } }
			public float MagnitudeSq { get { return (x * x) + (y * y); } }

			public IntVector2 FloorToMultipleOf(int multiple) {
				return new IntVector2(
					(int)Mathf.Floor(x / (float)multiple) * multiple,
					(int)Mathf.Floor(y / (float)multiple) * multiple
				);
			}

		}
	}
}