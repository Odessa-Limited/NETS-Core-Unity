using UnityEngine;

namespace OdessaEngine.NETS.Core {
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

		public enum LerpType {
			Velocity,
			Smooth,
			Linear,
		}

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
				lastReturnedValue = LinearLerp(previousValue, currentValue, percent);
			else if (type == LerpType.Velocity)
				lastReturnedValue = Bezier2(previousValue, previousValueAfterVelocity, currentValue, percent);
			return lastReturnedValue;
		}

	}

	public class Vector3AdaptiveLerp : AdaptiveLerp<Vector3> {
		protected override float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;

		protected override Vector3 LinearLerp(Vector3 a, Vector3 b, float value) => a + (b - a) * value;

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
}