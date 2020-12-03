using UnityEngine;

namespace OdessaEngine.NETS.Core {
	public enum LerpType {
		None,
		Velocity,
		Smooth,
		Linear,
	}

	public abstract class AdaptiveLerp<T> {
		T previousValue = default;
		T currentValue = default;
		T previousValueAfterVelocity = default;
		public float expectedVelocity = -1;
		public float lastReceiveTime;
		public float expectedReceiveDelay = -1;
		public float velocityExterpolationAmount = 1.2f;
		public float velocityCorrectionAmount = 0.1f;
		public float timeTolerance = 0.7f;
		public LerpType type = LerpType.Velocity;

		T lastReturnedValue = default;
		public float estimatedVelocityChangeBeforeTeleportSeconds = 3;

		public AdaptiveLerp(float expectedReceiveTime) {
			expectedReceiveDelay = expectedReceiveTime;
		}
		public AdaptiveLerp() { }

		protected abstract float Distance(T a, T b);
		protected abstract T Default();
		protected abstract T LinearLerp(T a, T b, float value);
		protected abstract T SoftLerp(T a, T b, float value);
		protected abstract T Bezier2(T a, T b, T c, float value);

		private void SetLastObject(T o) {
			previousValueAfterVelocity = LinearLerp(LinearLerp(previousValue, currentValue, velocityExterpolationAmount), o, velocityCorrectionAmount);
			previousValue = lastReturnedValue;// currentValue;
			currentValue = o;
			lastReceiveTime = Time.time;
		}

		public void ValueChanged(T input) {
			if (Equals(currentValue, Default())) {
				SetLastObject(input);
				previousValue = input;
				lastReturnedValue = input;
				currentValue = input;
				lastReceiveTime = Time.time;
				return;
			}

			var now = Time.time;
			var timeDifference = now - lastReceiveTime;

			var velocity = Distance(input, currentValue) * timeDifference;
			if (expectedVelocity == -1) expectedVelocity = velocity;

			// Outside estimate velocity
			if ((expectedVelocity * 0.9f < velocity && velocity < expectedVelocity * 1.1f) == false) {
				expectedVelocity = Mathf.Lerp(expectedVelocity, velocity, 0.3f);
			}

			// Needs to TP?
			if (velocity > expectedVelocity * estimatedVelocityChangeBeforeTeleportSeconds) {
				// teleport
				currentValue = input;
				lastReturnedValue = input;
				previousValue = input;
			}


			if (expectedReceiveDelay == -1) expectedReceiveDelay = timeDifference;
			else expectedReceiveDelay = Mathf.Lerp(expectedReceiveDelay, timeDifference, 0.3f);

			SetLastObject(input);
		}

		public T GetLerped() {
			var percent = ((Time.time - lastReceiveTime) / expectedReceiveDelay) * timeTolerance;
			percent = Mathf.Clamp(percent, 0, 1);
			if (type == LerpType.Smooth)
				lastReturnedValue = SoftLerp(previousValue, currentValue, percent);
			else if (type == LerpType.Linear)
				lastReturnedValue = LinearLerp(previousValue, currentValue, percent * 0.9f);
			else if (type == LerpType.Velocity)
				lastReturnedValue = Bezier2(previousValue, previousValueAfterVelocity, currentValue, percent);
			return lastReturnedValue;
		}

	}

	public class Vector3AdaptiveLerp : AdaptiveLerp<Vector3> {
		protected override float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;

		protected override Vector3 LinearLerp(Vector3 a, Vector3 b, float value) => Vector3.Lerp(a, b, value);

		protected override Vector3 SoftLerp(Vector3 a, Vector3 b, float value) => new Vector3(
			Mathf.SmoothStep(a.x, b.x, value),
			Mathf.SmoothStep(a.y, b.y, value),
			Mathf.SmoothStep(a.z, b.z, value)
		);

		protected override Vector3 Bezier2(Vector3 s, Vector3 p, Vector3 e, float t) {
			float rt = 1 - t;
			return rt * rt * s + 2 * rt * t * p + t * t * e;
		}

		protected override Vector3 Default() => Vector3.zero;
	}
	public class QuaternionAdaptiveLerp : AdaptiveLerp<Quaternion> {
		protected override float Distance(Quaternion a, Quaternion b) => Quaternion.Angle(a,b);

		protected override Quaternion LinearLerp(Quaternion a, Quaternion b, float value) => Quaternion.Slerp(a, b, value);

		protected override Quaternion SoftLerp(Quaternion a, Quaternion b, float value) => new Quaternion(
			Mathf.SmoothStep(a.x, b.x, value),
			Mathf.SmoothStep(a.y, b.y, value),
			Mathf.SmoothStep(a.z, b.z, value),
			Mathf.SmoothStep(a.w, b.w, value)
		);

		protected override Quaternion Bezier2(Quaternion s, Quaternion p, Quaternion e, float t) {
			return Quaternion.Slerp(Quaternion.Slerp(s, p, 2 * t * (1 - t)), e, t * t);
		}

		protected override Quaternion Default() => Quaternion.identity;
	}
}