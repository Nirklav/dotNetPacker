using System;
using System.Collections.Generic;

namespace Packer
{
  public class ArgsParser
  {
    public delegate bool TryParse<T>(string str, out T value);

    private Dictionary<string, string> _params;

    public ArgsParser(string[] args)
    {
      if (args.Length % 2 != 0)
        throw new InvalidOperationException("invalid args count");

      _params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
      for (int i = 0; i < args.Length; i += 2)
        _params.Add(args[i], args[i + 1]);
    }

    public string GetOrDefault(string key, string def)
    {
      if (!_params.TryGetValue(key, out string value))
        return def;
      return value;
    }

    public T GetOrDefault<T>(string key, TryParse<T> parser, T def)
    {
      var valueStr = GetOrDefault(key, null);
      if (valueStr == null)
        return def;

      if (!parser(valueStr, out T value))
        return def;
      return value;
    }

    public string Get(string key)
    {
      if (!_params.TryGetValue(key, out string value))
        throw new InvalidOperationException("Param not found: " + key);
      return value;
    }

    public T Get<T>(string key, TryParse<T> parser)
    {
      var valueStr = Get(key);
      if (!parser(valueStr, out T value))
        throw new InvalidOperationException("Can't cast " + key + " to " + typeof(T).Name);
      return value;
    }

    public string[] GetAll(string key)
    {
      var valueStr = Get(key);
      return valueStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public T[] GetAll<T>(string key, TryParse<T> parser)
    {
      var valueStrs = GetAll(key);

      var output = new T[valueStrs.Length];
      for (var i = 0; i < valueStrs.Length; i++)
      {
        var str = valueStrs[i];
        if (!parser(str, out T value))
          throw new InvalidOperationException("Can't cast " + key + " to " + typeof(T).Name);
        output[i] = value;
      }

      return output;
    }
  }
}
