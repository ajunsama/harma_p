using System;
using System.Collections.Generic;
using UnityEngine;

public class LevelVariableManager : MonoBehaviour
{
    private readonly Dictionary<string, object> _variables = new Dictionary<string, object>();
    private readonly Dictionary<string, List<Action>> _listeners = new Dictionary<string, List<Action>>();
    private readonly Dictionary<string, LevelVariableDefinition> _definitions = new Dictionary<string, LevelVariableDefinition>();

    public void Initialize(List<LevelVariableDefinition> definitions)
    {
        _variables.Clear();
        _listeners.Clear();
        _definitions.Clear();

        foreach (var def in definitions)
        {
            if (string.IsNullOrEmpty(def.variableName)) continue;

            object defaultValue = ParseValue(def.defaultValue, def.type);
            _variables[def.variableName] = defaultValue;
            _definitions[def.variableName] = def;
        }
    }

    public void SetVariable(string name, object value)
    {
        if (!_variables.ContainsKey(name))
        {
            Debug.LogWarning($"[LevelVariableManager] 变量'{name}'未定义，跳过设置");
            return;
        }

        _variables[name] = value;
        NotifyListeners(name);
    }

    public T GetVariable<T>(string name)
    {
        if (_variables.TryGetValue(name, out var val))
        {
            try { return (T)Convert.ChangeType(val, typeof(T)); }
            catch { return default; }
        }
        Debug.LogWarning($"[LevelVariableManager] 变量'{name}'不存在");
        return default;
    }

    public bool TryGetVariable(string name, out object value)
    {
        return _variables.TryGetValue(name, out value);
    }

    public bool CheckCondition(LevelVariableCondition c)
    {
        if (c == null || string.IsNullOrEmpty(c.variableName)) return true;

        if (!_variables.TryGetValue(c.variableName, out var current))
        {
            Debug.LogWarning($"[LevelVariableManager] 条件检查：变量'{c.variableName}'不存在");
            return false;
        }

        switch (c.mode)
        {
            case LevelVariableCondition.CompareMode.IsTrue:
                return current is bool b && b;
            case LevelVariableCondition.CompareMode.IsFalse:
                return current is bool b2 && !b2;
            default:
                return CompareValues(current, c.compareValue, c.mode);
        }
    }

    public bool CheckAllConditions(List<LevelVariableCondition> conditions)
    {
        if (conditions == null || conditions.Count == 0) return true;
        foreach (var c in conditions)
            if (!CheckCondition(c)) return false;
        return true;
    }

    public void OnVariableChanged(string name, Action callback)
    {
        if (!_listeners.ContainsKey(name))
            _listeners[name] = new List<Action>();
        _listeners[name].Add(callback);
    }

    public void RemoveListener(string name, Action callback)
    {
        if (_listeners.TryGetValue(name, out var list))
            list.Remove(callback);
    }

    public void ApplySetAction(VariableSetAction action)
    {
        if (action == null || string.IsNullOrEmpty(action.variableName)) return;
        if (!_variables.ContainsKey(action.variableName))
        {
            Debug.LogWarning($"[LevelVariableManager] ApplySetAction: 变量'{action.variableName}'未定义");
            return;
        }

        var def = FindDefinition(action.variableName);
        if (def != null)
        {
            object val = ParseValue(action.stringValue, def.type);
            SetVariable(action.variableName, val);
        }
    }

    public void ApplySetActions(List<VariableSetAction> actions)
    {
        if (actions == null) return;
        foreach (var a in actions)
            ApplySetAction(a);
    }

    // ========== 内部 ==========

    private void NotifyListeners(string name)
    {
        if (_listeners.TryGetValue(name, out var list))
        {
            foreach (var cb in list)
                cb?.Invoke();
        }
        if (_listeners.TryGetValue("*", out var wildcardList))
        {
            foreach (var cb in wildcardList)
                cb?.Invoke();
        }
    }

    private LevelVariableDefinition FindDefinition(string name)
    {
        if (_definitions.TryGetValue(name, out var def))
            return def;
        Debug.LogWarning($"[LevelVariableManager] 变量'{name}'的定义不存在");
        return null;
    }

    public static object ParseValue(string raw, LevelVariableType type)
    {
        if (string.IsNullOrEmpty(raw)) return GetDefault(type);

        try
        {
            switch (type)
            {
                case LevelVariableType.Bool: return bool.Parse(raw);
                case LevelVariableType.Int: return int.Parse(raw);
                case LevelVariableType.Float: return float.Parse(raw);
                case LevelVariableType.String: return raw;
                default: return raw;
            }
        }
        catch
        {
            return GetDefault(type);
        }
    }

    public static object GetDefault(LevelVariableType type)
    {
        switch (type)
        {
            case LevelVariableType.Bool: return false;
            case LevelVariableType.Int: return 0;
            case LevelVariableType.Float: return 0f;
            case LevelVariableType.String: return "";
            default: return null;
        }
    }

    private bool CompareValues(object current, string compareValue, LevelVariableCondition.CompareMode mode)
    {
        if (current is bool cb)
        {
            if (bool.TryParse(compareValue, out var tb))
            {
                switch (mode)
                {
                    case LevelVariableCondition.CompareMode.Equals: return cb == tb;
                    case LevelVariableCondition.CompareMode.NotEquals: return cb != tb;
                }
            }
            return false;
        }

        if (current is int ci)
        {
            if (int.TryParse(compareValue, out var ti))
            {
                switch (mode)
                {
                    case LevelVariableCondition.CompareMode.Equals: return ci == ti;
                    case LevelVariableCondition.CompareMode.NotEquals: return ci != ti;
                    case LevelVariableCondition.CompareMode.Greater: return ci > ti;
                    case LevelVariableCondition.CompareMode.GreaterOrEqual: return ci >= ti;
                    case LevelVariableCondition.CompareMode.Less: return ci < ti;
                    case LevelVariableCondition.CompareMode.LessOrEqual: return ci <= ti;
                }
            }
            return false;
        }

        if (current is float cf)
        {
            if (float.TryParse(compareValue, out var tf))
            {
                switch (mode)
                {
                    case LevelVariableCondition.CompareMode.Equals: return Mathf.Approximately(cf, tf);
                    case LevelVariableCondition.CompareMode.NotEquals: return !Mathf.Approximately(cf, tf);
                    case LevelVariableCondition.CompareMode.Greater: return cf > tf;
                    case LevelVariableCondition.CompareMode.GreaterOrEqual: return cf >= tf;
                    case LevelVariableCondition.CompareMode.Less: return cf < tf;
                    case LevelVariableCondition.CompareMode.LessOrEqual: return cf <= tf;
                }
            }
            return false;
        }

        if (current is string cs)
        {
            switch (mode)
            {
                case LevelVariableCondition.CompareMode.Equals: return cs == compareValue;
                case LevelVariableCondition.CompareMode.NotEquals: return cs != compareValue;
                case LevelVariableCondition.CompareMode.Contains: return cs.Contains(compareValue);
            }
            return false;
        }

        return false;
    }
}
