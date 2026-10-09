using System;
using System.Reflection;
using UnityEngine;

namespace MostWanted.Render
{
    /// <summary>
    /// Yansıma yardımcıları: URP / Unity sürümüne göre bulunmayabilecek özellik ve volume parametrelerine
    /// güvenli erişim. Hiçbiri istisna fırlatmaz; başarısızsa false döner.
    /// </summary>
    public static class RR
    {
        const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        static object Convert(object value, Type t)
        {
            if (value == null) return null;
            if (t.IsInstanceOfType(value)) return value;
            if (t.IsEnum)
            {
                string s = value as string;
                if (s != null) return Enum.IsDefined(t, s) ? Enum.Parse(t, s) : null;
                return Enum.ToObject(t, value);
            }
            return System.Convert.ChangeType(value, t);
        }

        /// <summary>Özellik ya da alan ata (enum değerleri isimle verilebilir).</summary>
        public static bool Set(object o, string name, object value)
        {
            if (o == null) return false;
            try
            {
                for (var t = o.GetType(); t != null; t = t.BaseType)
                {
                    var p = t.GetProperty(name, F | BindingFlags.DeclaredOnly);
                    if (p != null && p.CanWrite)
                    {
                        var v = Convert(value, p.PropertyType);
                        if (v == null && value != null) return false;
                        p.SetValue(o, v, null); return true;
                    }
                    var f = t.GetField(name, F | BindingFlags.DeclaredOnly);
                    if (f != null)
                    {
                        var v = Convert(value, f.FieldType);
                        if (v == null && value != null) return false;
                        f.SetValue(o, v); return true;
                    }
                }
            }
            catch (Exception) { }
            return false;
        }

        public static object Get(object o, string name)
        {
            if (o == null) return null;
            try
            {
                for (var t = o.GetType(); t != null; t = t.BaseType)
                {
                    var p = t.GetProperty(name, F | BindingFlags.DeclaredOnly);
                    if (p != null && p.CanRead && p.GetIndexParameters().Length == 0) return p.GetValue(o, null);
                    var f = t.GetField(name, F | BindingFlags.DeclaredOnly);
                    if (f != null) return f.GetValue(o);
                }
            }
            catch (Exception) { }
            return null;
        }

        public static bool SetStatic(Type t, string name, object value)
        {
            if (t == null) return false;
            try
            {
                var p = t.GetProperty(name, S);
                if (p != null && p.CanWrite) { p.SetValue(null, Convert(value, p.PropertyType), null); return true; }
                var f = t.GetField(name, S);
                if (f != null) { f.SetValue(null, Convert(value, f.FieldType)); return true; }
            }
            catch (Exception) { }
            return false;
        }

        /// <summary>Volume bileşeni parametresi: param.Override(value) (alan adı, ör. "dirtTexture").</summary>
        public static bool Param(object comp, string field, object value)
        {
            if (comp == null) return false;
            try
            {
                var f = comp.GetType().GetField(field, F);
                if (f == null) return false;
                var prm = f.GetValue(comp);
                if (prm == null) return false;
                // VolumeParameter<T>: T'yi "value" özelliğinden bul
                var vp = prm.GetType().GetProperty("value", F);
                if (vp == null) return false;
                var v = Convert(value, vp.PropertyType);
                if (v == null && value != null) return false;
                var ov = prm.GetType().GetMethod("Override", F, null, new[] { vp.PropertyType }, null);
                if (ov != null) ov.Invoke(prm, new[] { v });
                else
                {
                    vp.SetValue(prm, v, null);
                    var os = prm.GetType().GetProperty("overrideState", F);
                    if (os != null) os.SetValue(prm, true, null);
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>Tür adıyla tür bul (verilen bir türün derlemesinde ya da yüklü tüm derlemelerde).</summary>
        public static Type FindType(string fullName, Type sameAssemblyAs = null)
        {
            try
            {
                if (sameAssemblyAs != null)
                {
                    var t = sameAssemblyAs.Assembly.GetType(fullName);
                    if (t != null) return t;
                }
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = a.GetType(fullName);
                    if (t != null) return t;
                }
            }
            catch (Exception) { }
            return null;
        }

        public static object Call(object o, string method, params object[] args)
        {
            if (o == null) return null;
            try
            {
                foreach (var m in o.GetType().GetMethods(F))
                {
                    if (m.Name != method || m.GetParameters().Length != args.Length) continue;
                    return m.Invoke(o, args);
                }
            }
            catch (Exception) { }
            return null;
        }

        // ---- Shader globalleri (stub'da olmayan Shader.SetGlobal* için önbellekli yansıma) ----
        static MethodInfo sgFloat, sgVector, sgColor, sgTex;
        static bool sgInit;
        static void InitSG()
        {
            if (sgInit) return; sgInit = true;
            try
            {
                var t = typeof(Shader);
                sgFloat = t.GetMethod("SetGlobalFloat", S, null, new[] { typeof(string), typeof(float) }, null);
                sgVector = t.GetMethod("SetGlobalVector", S, null, new[] { typeof(string), typeof(Vector4) }, null);
                sgColor = t.GetMethod("SetGlobalColor", S, null, new[] { typeof(string), typeof(Color) }, null);
                sgTex = t.GetMethod("SetGlobalTexture", S, null, new[] { typeof(string), typeof(Texture) }, null);
            }
            catch (Exception) { }
        }
        public static void GlobalFloat(string n, float v) { InitSG(); if (sgFloat != null) try { sgFloat.Invoke(null, new object[] { n, v }); } catch (Exception) { } }
        public static void GlobalVector(string n, Vector4 v) { InitSG(); if (sgVector != null) try { sgVector.Invoke(null, new object[] { n, v }); } catch (Exception) { } }
        public static void GlobalColor(string n, Color v) { InitSG(); if (sgColor != null) try { sgColor.Invoke(null, new object[] { n, v }); } catch (Exception) { } }
        public static void GlobalTexture(string n, Texture v) { InitSG(); if (sgTex != null) try { sgTex.Invoke(null, new object[] { n, v }); } catch (Exception) { } }
    }
}
