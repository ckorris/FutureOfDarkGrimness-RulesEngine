using System;
using System.Collections.Generic;

namespace FDG
{
    public class Entity
    {
        private Dictionary<Type, IComponent> _components = new();

        public void AddComponent<T>() where T : IComponent, new()
        {
            AssertComponentTypeNotYetAdded<T>();

            _components.Add(typeof(T), new T());

        }

        public void AddComponent<T>(T componentToAdd) where T : IComponent
        {
            AssertComponentTypeNotYetAdded<T>();

            _components.Add(typeof(T), componentToAdd);
        }

        public T GetComponent<T>() where T : IComponent
        {
            if (_components.TryGetValue(typeof(T), out IComponent component))
            {
                return (T)component;
            }

            throw new MissingComponentException($"Entity didn't have component of type {typeof(T)}.");
        }

        public bool TryGetComponent<T>(out T component) where T : IComponent
        {
            if (HasComponent<T>() == false)
            {
                component = default;
                return false;
            }

            component = GetComponent<T>();
            return true;
        }

        public bool HasComponent<T>() where T : IComponent
        {
            return _components.ContainsKey(typeof(T));
        }


        private void AssertComponentTypeNotYetAdded<T>() where T : IComponent
        {
            Type componentType = typeof(T);

            if (_components.ContainsKey(componentType))
            {
                throw new ComponentAlreadyAddedException($"Tried to add component of type {componentType} to entity, but it already had one.");
            }
        }

        public class MissingComponentException : Exception
        {
            public MissingComponentException(string message)
                : base(message) { }
        }

        public class ComponentAlreadyAddedException : Exception
        {
            public ComponentAlreadyAddedException(string message)
                : base(message) { }
        }
    }
}
