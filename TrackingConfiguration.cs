﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Linq.Expressions;
using SettingsTracking.Attributes;
using System.ComponentModel;
using SettingsTracking.DataStoring;

namespace SettingsTracking
{
    public enum PersistModes
    {
        /// <summary>
        /// State is persisted automatically upon application close
        /// </summary>
        Automatic,
        /// <summary>
        /// State is persisted only upon request
        /// </summary>
        Manual
    }

    //public static class PropertyInfoExtensions
    //{
    //    public static Func<T, object> GetValueGetter<T>(this PropertyInfo propertyInfo)
    //    {
    //        if (typeof(T) != propertyInfo.DeclaringType)
    //        {
    //            throw new ArgumentException();
    //        }

    //        var instance = Expression.Parameter(propertyInfo.DeclaringType, "i");
    //        var property = Expression.Property(instance, propertyInfo);
    //        var convert = Expression.TypeAs(property, typeof(object));
    //        return (Func<T, object>)Expression.Lambda(convert, instance).Compile();
    //    }

    //    public static Action<T, object> GetValueSetter<T>(this PropertyInfo propertyInfo)
    //    {
    //        if (typeof(T) != propertyInfo.DeclaringType)
    //        {
    //            throw new ArgumentException();
    //        }

    //        var instance = Expression.Parameter(propertyInfo.DeclaringType, "i");
    //        var argument = Expression.Parameter(typeof(object), "a");
    //        var setterCall = Expression.Call(
    //            instance,
    //            propertyInfo.GetSetMethod(),
    //            Expression.Convert(argument, propertyInfo.PropertyType));
    //        return (Action<T, object>)Expression.Lambda(setterCall, instance, argument).Compile();
    //    }
    //}

    public sealed class TrackingConfiguration
    {
        sealed class TypeTrackingMetaData
        {
            public string Context { get; private set; }
            public string KeyPropertyName { get; private set; }
            public IEnumerable<string> PropertyNames { get; private set; }

            public TypeTrackingMetaData(string context, string keyPropertyName, IEnumerable<string> propertyNames)
            {
                Context = context;
                KeyPropertyName = keyPropertyName;
                PropertyNames = propertyNames;
            }
        }

        private SettingsTracker _tracker;

        public string Key { get; set; }
        public HashSet<string> Properties { get; set; }
        public WeakReference TargetReference { get; private set; }
        public PersistModes Mode { get; set; }

        #region apply/persist events
        public event EventHandler<TrackingOperationEventArgs> ApplyingState;
        private bool OnApplyingState()
        {
            if (ApplyingState != null)
            {
                TrackingOperationEventArgs args = new TrackingOperationEventArgs(this);
                ApplyingState(this, args);
                return !args.Cancel;
            }
            else
                return true;
        }

        public event EventHandler AppliedState;
        private void OnAppliedState()
        {
            if (AppliedState != null)
                AppliedState(this, EventArgs.Empty);
        }

        public event EventHandler<TrackingOperationEventArgs> PersistingState;
        private bool OnPersistingState()
        {
            if (PersistingState != null)
            {
                TrackingOperationEventArgs args = new TrackingOperationEventArgs(this);
                PersistingState(this, args);
                return !args.Cancel;
            }
            return true;
        }

        public event EventHandler PersistedState;
        private void OnPersistedState()
        {
            if (PersistedState != null)
                PersistedState(this, EventArgs.Empty);
        }
        #endregion

        internal TrackingConfiguration(object target, SettingsTracker tracker)
        {
            _tracker = tracker;
            this.TargetReference = new WeakReference(target);
            Properties = new HashSet<string>();
            AddMetaData();

            ITrackingAware trackingAwareTarget = target as ITrackingAware;
            if (trackingAwareTarget != null)
                trackingAwareTarget.InitTracking(this);

            IRaiseTrackingNotifier asNotify = target as IRaiseTrackingNotifier;
            if (asNotify != null)
                asNotify.SettingsPersistRequest += (s, e) => Persist();
        }

        public void Persist()
        {
            if (TargetReference.IsAlive && OnPersistingState())
            {
                foreach (string propertyName in Properties)
                {
                    PropertyInfo property = TargetReference.Target.GetType().GetProperty(propertyName);

                    string propKey = ConstructPropertyKey(property.Name);
                    try
                    {
                        object currentValue = property.GetValue(TargetReference.Target, null);
                        _tracker.ObjectStore.Persist(currentValue, propKey);
                    }
                    catch
                    {
                        Debug.WriteLine("Persisting of value '{propKey}' failed!");
                    }
                }

                OnPersistedState();
            }
        }

        bool _applied = false;
        [DebuggerHidden]
        public void Apply()
        {
            Stopwatch sw = new Stopwatch();
            
            if (TargetReference.IsAlive && OnApplyingState())
            {
                foreach (string propertyName in Properties)
                {
                    sw.Restart();
                    PropertyInfo property = TargetReference.Target.GetType().GetProperty(propertyName);
                    string propKey = ConstructPropertyKey(property.Name);
                    try
                    {
                        if (_tracker.ObjectStore.ContainsKey(propKey))
                        {
                            object storedValue = _tracker.ObjectStore.Retrieve(propKey);
                            property.SetValue(TargetReference.Target, storedValue, null);
                        }
                    }
                    catch(Exception ex)
                    {
                        Trace.WriteLine(string.Format("TRACKING: Applying tracking to property with key='{0}' failed. ExceptionType:'{1}', message: '{2}'!", propKey, ex.GetType().Name, ex.Message));
                    }
                }

                OnAppliedState();
            }
            _applied = true;
        }

        public TrackingConfiguration AddProperties(params string[] properties)
        {
            foreach (string property in properties)
                Properties.Add(property);
            return this;
        }
        public TrackingConfiguration AddProperties<T>(params Expression<Func<T, object>>[] properties)
        {
            AddProperties(properties.Select(p => GetPropertyNameFromExpression(p)).ToArray());
            return this;
        }
        public TrackingConfiguration RemoveProperties(params string[] properties)
        {
            foreach (string property in properties)
                Properties.Remove(property);
            return this;
        }
        public TrackingConfiguration RemoveProperties<T>(params Expression<Func<T, object>>[] properties)
        {
            RemoveProperties(properties.Select(p => GetPropertyNameFromExpression(p)).ToArray());
            return this;
        }

        public TrackingConfiguration RegisterPersistTrigger(string eventName)
        {
            return RegisterPersistTrigger(eventName, TargetReference.Target);
        }

        public TrackingConfiguration RegisterPersistTrigger(string eventName, object eventSourceObject)
        {
            Mode = PersistModes.Manual;

            EventInfo eventInfo = eventSourceObject.GetType().GetEvent(eventName);
            var parameters = eventInfo.EventHandlerType
                .GetMethod("Invoke")
                .GetParameters()
                .Select(parameter => Expression.Parameter(parameter.ParameterType))
                .ToArray();

            var handler = Expression.Lambda(
                    eventInfo.EventHandlerType,
                    Expression.Call(Expression.Constant(new Action(() => { if (_applied) Persist(); })), "Invoke", Type.EmptyTypes),
                    parameters)
              .Compile();

            eventInfo.AddEventHandler(eventSourceObject, handler);
            return this;
        }

        public TrackingConfiguration SetMode(PersistModes mode)
        {
            this.Mode = mode;
            return this;
        }

        public TrackingConfiguration SetKey(string key)
        {
            this.Key = key;
            return this;
        }

        private TrackingConfiguration AddMetaData()
        {
            Type t = TargetReference.Target.GetType();
            TypeTrackingMetaData metadata = GetTypeData(t, _tracker != null ? _tracker.Name : null);

            if (!string.IsNullOrEmpty(metadata.KeyPropertyName))
                Key = t.GetProperty(metadata.KeyPropertyName).GetValue(TargetReference.Target, null).ToString();
            foreach (string propName in metadata.PropertyNames)
                this.Properties.Add(propName);
            return this;
        }

        //cache of type data for each type/context pair
        static Dictionary<Tuple<Type, string>, TypeTrackingMetaData> _typeMetadataCache = new Dictionary<Tuple<Type, string>, TypeTrackingMetaData>();
        private static TypeTrackingMetaData GetTypeData(Type t, string trackerName)
        {
            Tuple<Type, string> _key = new Tuple<Type, string>(t, trackerName);
            if (!_typeMetadataCache.ContainsKey(_key))
            {
                PropertyInfo keyProperty = t.GetProperties().SingleOrDefault(pi => pi.IsDefined(typeof(TrackingKeyAttribute), true));

                //see if TrackableAttribute(true) exists on the target class
                bool isClassMarkedAsTrackable = false;
                TrackableAttribute targetClassTrackableAtt = t.GetCustomAttributes(true).OfType<TrackableAttribute>().Where(ta => ta.TrackerName == trackerName).FirstOrDefault();
                if (targetClassTrackableAtt != null && targetClassTrackableAtt.IsTrackable)
                    isClassMarkedAsTrackable = true;

                //add properties that need to be tracked
                List<string> properties = new List<string>();
                foreach (PropertyInfo pi in t.GetProperties())
                {
                    //don't track the key property
                    if (pi == keyProperty)
                        continue;

                    TrackableAttribute propTrackableAtt = pi.GetCustomAttributes(true).OfType<TrackableAttribute>().Where(ta => ta.TrackerName == trackerName).FirstOrDefault();
                    if (propTrackableAtt == null)
                    {
                        //if the property is not marked with Trackable(true), check if the class is
                        if (isClassMarkedAsTrackable)
                            properties.Add(pi.Name);
                    }
                    else
                    {
                        if (propTrackableAtt.IsTrackable)
                            properties.Add(pi.Name);
                    }
                }

                string keyName = null;
                if (keyProperty != null)
                    keyName = keyProperty.Name;
                _typeMetadataCache[_key] = new TypeTrackingMetaData(trackerName, keyName, properties);
            }
            return _typeMetadataCache[_key];
        }

        private static string GetPropertyNameFromExpression<T>(Expression<Func<T, object>> exp)
        {
            MemberExpression membershipExpression;
            if (exp.Body is UnaryExpression)
                membershipExpression = (exp.Body as UnaryExpression).Operand as MemberExpression;
            else
                membershipExpression = exp.Body as MemberExpression;
            return membershipExpression.Member.Name;
        }

        private string ConstructPropertyKey(string propertyName)
        {
            return string.Format("{0}_{1}.{2}", TargetReference.Target.GetType().Name, Key, propertyName);
        }
    }
}