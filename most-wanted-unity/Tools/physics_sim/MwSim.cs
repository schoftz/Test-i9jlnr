// "MW Sürüş" testi (oyundaki MwDrive fonksiyonları): kararlı hal dönüş yarıçapı, oturma süresi, spin yok,
// 90° sokak köşesi (12 m genişlik, sağa dönüş) hangi hızlarda alınabiliyor.
// Derleme: mcs MwSim.cs ../../Assets/Scripts/MwDrive.cs && mono MwSim.exe
using System; using MostWanted;
class MwSim {
  const float dt = 1f / 60f;
  class S { public float x, z, psi, lat, fwd, w; }
  // psi: +z'den saat yönü (sağa) radyan; ileri = (sin psi, cos psi), sağ = (cos psi, -sin psi)
  static void Step(S s, float steer, float holdKmh, float brake, float hb) {
    float kmh = (float)Math.Sqrt(s.lat * s.lat + s.fwd * s.fwd) * 3.6f;
    float target = MwDrive.TargetYaw(steer, s.fwd, 1f, brake > 0f, hb);
    s.w = MwDrive.StepYaw(s.w, target, dt);
    // dönen eksen takımı: dünya hızı sabitken araç dönünce yanal hız oluşur
    float lat = s.lat - s.w * s.fwd * dt, fw = s.fwd + s.w * s.lat * dt;
    MwDrive.StepVelocity(ref lat, ref fw, s.w, kmh, hb, dt);
    if (holdKmh > 0f) fw += Math.Max(-3f, Math.Min(5f, (holdKmh / 3.6f - fw) * 3f)) * dt;
    if (brake > 0f) fw = Math.Max(0f, fw - brake * dt);
    s.lat = lat; s.fwd = fw;
    s.psi += s.w * dt;
    float sx = (float)Math.Sin(s.psi), cz = (float)Math.Cos(s.psi);
    s.x += (s.fwd * sx + s.lat * cz) * dt;
    s.z += (s.fwd * cz - s.lat * sx) * dt;
  }
  static void Main() {
    Console.WriteLine("MW Sürüş — kararlı hal tam kilit (hız sabit tutulur):");
    Console.WriteLine(" km/s | yarıçap (m) | hedef | kayma β (°) | %90 savrulmaya ulaşma (sn)");
    float[] tgt = { 5, 12, 27, 40 };
    int k = 0;
    foreach (float kmh in new[] { 30f, 60f, 100f, 120f }) {
      var s = new S { fwd = kmh / 3.6f }; float t90 = -1, rs = 0, n = 0, beta = 0;
      float tw = MwDrive.TargetYaw(1f, s.fwd, 1f, false, 0f);
      for (float t = 0; t < 3f; t += dt) {
        Step(s, 1f, kmh, 0f, 0f);
        if (t90 < 0 && s.w >= 0.9f * tw) t90 = t;
        if (t > 2.5f) { rs += (float)Math.Sqrt(s.lat * s.lat + s.fwd * s.fwd) / s.w; n++; beta = (float)(Math.Atan2(-s.lat, s.fwd) * 180 / Math.PI); }
      }
      Console.WriteLine(" {0,4:0} | {1,8:0.0}    | ≈{2,3:0}  | {3,7:0.0}     | {4:0.00}", kmh, rs / n, tgt[k++], beta, t90);
    }
    // spin testi: 120 km/s ani sağ-sol slalom (el freni yok)
    { var s = new S { fwd = 120f / 3.6f }; float maxB = 0;
      for (float t = 0; t < 4f; t += dt) { Step(s, ((int)(t / 0.6f)) % 2 == 0 ? 1f : -1f, 0f, 0f, 0f); maxB = Math.Max(maxB, (float)Math.Abs(Math.Atan2(s.lat, s.fwd) * 180 / Math.PI)); }
      Console.WriteLine("\nSlalom 120 km/s (0.6 sn'de bir tam sağ/sol, el freni yok): maks. kayma {0:0.0}° → spin {1}", maxB, maxB > 45 ? "VAR" : "yok"); }
    // el freni drifti: 70 km/s, tam sağ + el freni 1.2 sn, sonra karşı direksiyon
    { var s = new S { fwd = 70f / 3.6f }; float maxB = 0;
      for (float t = 0; t < 3f; t += dt) { bool hb = t < 1.2f; Step(s, t < 1.2f ? 1f : -0.4f, 0f, 0f, hb ? 1f : 0f); maxB = Math.Max(maxB, (float)Math.Abs(Math.Atan2(s.lat, s.fwd) * 180 / Math.PI)); }
      Console.WriteLine("El freni 70 km/s 1.2 sn: maks. kayma {0:0.0}°, sonra karşı direksiyonla toparlandı: kayma {1:0.0}°, hız {2:0} km/s", maxB, Math.Atan2(s.lat, s.fwd) * 180 / Math.PI, Math.Sqrt(s.lat * s.lat + s.fwd * s.fwd) * 3.6); }
    // 90° hız kaybı
    { var s = new S { fwd = 80f / 3.6f }; while (s.psi < Math.PI / 2) Step(s, 1f, 0f, 0f, 0f);
      Console.WriteLine("80 km/s'den gazsız 90° dönüş sonrası hız: {0:0.0} km/s (kayıp %{1:0.0})", Math.Sqrt(s.lat * s.lat + s.fwd * s.fwd) * 3.6, (1 - Math.Sqrt(s.lat * s.lat + s.fwd * s.fwd) * 3.6 / 80) * 100); }
    // sokak köşesi
    Console.WriteLine("\n90° sağa dönüş, 12 m genişlik yollar (sağ şeritten giriş, en iyi dönüş noktası aranır):");
    foreach (float kmh in new[] { 50f, 60f, 70f, 80f, 90f, 100f, 110f, 120f })
      Console.WriteLine("  {0,3:0} km/s: gazda {1}   frenle (8 m/s²) {2}", kmh, Corner(kmh, false) ? "ALINIR" : "alınamaz", Corner(kmh, true) ? "ALINIR" : "alınamaz");
  }
  static bool Inside(float x, float z, float Z0) { return (Math.Abs(x) <= 6f && z <= Z0 + 6f) || (x >= -6f && Math.Abs(z - Z0) <= 6f); }
  static bool Corner(float kmh, bool brake) {
    const float Z0 = 100f;
    for (float start = Z0 - 45f; start <= Z0 + 3f; start += 0.25f) {
      var s = new S { x = 3f, z = 0f, fwd = kmh / 3.6f };
      bool ok = true, turning = false, done = false;
      for (int i = 0; i < 2400 && ok; i++) {
        float steer = 0f, br = 0f;
        if (s.z >= start && !done) { turning = true; }
        if (turning && s.psi >= Math.PI / 2 - 0.02) { turning = false; done = true; }
        if (turning) steer = 1f;
        else if (done) steer = (float)Math.Max(-1, Math.Min(1, ((Z0 - 3f) - s.z) * -0.15f - (s.psi - Math.PI / 2) * 1.5f));   // şeride otur
        if (brake && !done && s.z > start - 25f && s.z < start + 2f && s.fwd > 50f / 3.6f) br = 8f;   // en fazla 50 km/s’ye kadar fren
        Step(s, steer, 0f, br, 0f);
        if (!Inside(s.x, s.z, Z0)) ok = false;
        if (s.x > 60f) break;
      }
      if (ok && s.x > 60f) return true;
    }
    return false;
  }
}
