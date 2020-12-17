using System;
using System.Collections.Generic;

namespace OdessaEngine.NETS.Core {
	public class NetsObjectPool<T> where T : new() {

		private Stack<T> pool = new Stack<T>();
		private List<T> objects = new List<T>();
		private Func<T> objectFactory;

		private Action<T> objectInitializer;
		private Action<T> objectDeinitializer;

		public NetsObjectPool() {

		}

		public NetsObjectPool(Func<T> objectFactory) {
			this.objectFactory = objectFactory;
		}

		public void SetInitializer(Action<T> initializer) {
			objectInitializer = initializer;
		}

		public void SetDeinitializer(Action<T> deinitializer) {
			objectDeinitializer = deinitializer;
		}


		public T Get() {
			T obj = default(T);
			if (pool.Count > 0) {
				obj = pool.Pop();
			} else {
				if (objectFactory != null) {
					obj = objectFactory();
				} else {
					obj = new T();
				}
				objects.Add(obj);
			}
			if (objectInitializer != null) {
				objectInitializer(obj);
			}
			return obj;
		}

		public void Return(T obj) {
			if (pool.Contains(obj) == false) {
				if (objectDeinitializer != null)
					objectDeinitializer(obj);
				pool.Push(obj);
			}

		}

		public void ReturnAll() {
			for (int i = 0; i < objects.Count; i++) {
				Return(objects[i]);
			}
		}

	}
}