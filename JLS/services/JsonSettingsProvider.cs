using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace JLS.Services
{
    public class JsonSettingsProvider : SettingsProvider
    {
        private readonly string _filePath;

        public JsonSettingsProvider()
        {
            _filePath = AppPaths.AppSettingsJson;
        }

        public override string ApplicationName { get; set; } = "JLSPlayer";

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(this.ApplicationName, config);
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            var values = new SettingsPropertyValueCollection();
            Dictionary<string, string>? jsonDict = null;

            if (File.Exists(_filePath))
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new StreamReader(stream))
                        {
                            string json = reader.ReadToEnd();
                            if (!string.IsNullOrEmpty(json))
                            {
                                jsonDict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                            }
                        }
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(15);
                    }
                    catch { break; }
                }
            }

            jsonDict ??= new Dictionary<string, string>();

            foreach (SettingsProperty prop in collection)
            {
                var val = new SettingsPropertyValue(prop);
                
                if (jsonDict.TryGetValue(prop.Name, out string? strVal) && strVal != null)
                {
                    try
                    {
                        val.PropertyValue = TypeDescriptor.GetConverter(prop.PropertyType).ConvertFromInvariantString(strVal);
                    }
                    catch
                    {
                        val.PropertyValue = GetSafeDefaultValue(prop);
                    }
                }
                else
                {
                    val.PropertyValue = GetSafeDefaultValue(prop);
                }
                
                val.IsDirty = false;
                val.Deserialized = true;                
                
                values.Add(val);
            }

            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            var jsonDict = new Dictionary<string, string>();
            bool loadedSuccessfully = false;

            if (File.Exists(_filePath))
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        using (var reader = new StreamReader(stream))
                        {
                            string json = reader.ReadToEnd();
                            if (!string.IsNullOrEmpty(json))
                            {
                                jsonDict = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? jsonDict;
                                loadedSuccessfully = true;
                            }
                        }
                        break;
                    }
                    catch (IOException)
                    {
                        Thread.Sleep(15);
                    }
                    catch { break; }
                }
            }
            else
            {
                loadedSuccessfully = true; 
            }

            if (!loadedSuccessfully && File.Exists(_filePath))
            {
                return; 
            }

            foreach (SettingsPropertyValue val in collection)
            {
                if (val.PropertyValue != null)
                {
                    try
                    {
                        string? strVal = TypeDescriptor.GetConverter(val.Property.PropertyType).ConvertToInvariantString(val.PropertyValue);
                        if (strVal != null)
                        {
                            jsonDict[val.Name] = strVal;
                        }
                    }
                    catch { }
                }
            }

            for (int i = 0; i < 5; i++)
            {
                try
                {
                    string outputJson = JsonSerializer.Serialize(jsonDict, new JsonSerializerOptions { WriteIndented = true });
                    using (var stream = new FileStream(_filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(outputJson);
                    }
                    break;
                }
                catch (IOException)
                {
                    Thread.Sleep(15);
                }
                catch { break; }
            }
        }

        private object? GetSafeDefaultValue(SettingsProperty prop)
        {
            if (prop.DefaultValue == null) return null;

            if (prop.DefaultValue is string str && prop.PropertyType != typeof(string))
            {
                try
                {
                    return TypeDescriptor.GetConverter(prop.PropertyType).ConvertFromInvariantString(str);
                }
                catch { return null; }
            }

            return prop.DefaultValue;
        }
    }
}