using UnityEngine;

namespace MostWanted
{
    /// <summary>Kameralar: yakın/uzak takip, kaput, tampon. Gecikme, yatma, hıza göre FOV, sarsıntı.</summary>
    public class CameraRig : MonoBehaviour
    {
        public enum Mode { ChaseNear, ChaseFar, Hood, Bumper }
        public Mode mode = Mode.ChaseNear;
        public Camera cam;
        public CarController target;
        public static readonly string[] ModeNames = { "Yakın Takip", "Uzak Takip", "Kaput", "Tampon" };

        Vector3 vel;
        float fov = 60f, lean, shake;
        Vector3 lastV;
        float latAccel;
        Vector3 lookDir;

        public void Shake(float amount) { shake = Mathf.Max(shake, amount); }

        public void Next()
        {
            mode = (Mode)(((int)mode + 1) % 4);
            if (Game.I != null) Game.I.Toast("Kamera: " + ModeNames[(int)mode]);
        }

        public void Snap()
        {
            if (target == null) return;
            var t = target.transform;
            cam.transform.position = t.position - t.forward * 7f + Vector3.up * 2.5f;
            cam.transform.LookAt(t.position + Vector3.up);
            vel = Vector3.zero;
        }

        void LateUpdate()
        {
            if (target == null || cam == null) return;
            var t = target.transform;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            float kmh = target.SpeedKmh;
            float sp01 = Mathf.Clamp01(kmh / 280f);

            Vector3 v = U.Vel(target.rb);
            Vector3 acc = (v - lastV) / Mathf.Max(Time.deltaTime, 0.0001f);
            lastV = v;
            if (Time.deltaTime > 0f) latAccel = Mathf.Lerp(latAccel, target.latG * CarMath.Gravity, 1f - Mathf.Exp(-dt * 6f));
            float latG = latAccel / CarMath.Gravity;

            float targetFov = 58f + sp01 * 16f + (target.nitroActive ? 12f : 0f) + Mathf.Clamp(Mathf.Abs(latG) - 0.6f, 0f, 1.5f) * 3f;   // yüksek yanal g'de FOV nabzı
            if (target.tyreSlip > 1.2f && kmh > 30f) Shake(Mathf.Clamp01((target.tyreSlip - 1.2f) * 0.08f));
            var pd = target.GetComponent<PlayerDriver>();
            if (pd != null && pd.speedbreakerOn) targetFov -= 6f;

            shake = Mathf.Max(0f, shake - dt * 2.5f);
            float sh = shake + (target.nitroActive ? 0.06f : 0f) + (kmh > 200f ? (kmh - 200f) / 2000f : 0f);
            Vector3 shakeOff = new Vector3(Mathf.PerlinNoise(Time.unscaledTime * 25f, 0f) - 0.5f, Mathf.PerlinNoise(0f, Time.unscaledTime * 25f) - 0.5f, 0f) * sh;

            if (mode == Mode.Hood || mode == Mode.Bumper)
            {
                var b = target.transform.Find("Gorsel");
                float L = target.def != null ? target.def.length : 4.5f;
                Vector3 lp = mode == Mode.Hood ? new Vector3(0, 1.25f, 0.2f) : new Vector3(0, 0.75f, L * 0.5f + 0.1f);
                cam.transform.position = t.TransformPoint(lp) + t.TransformVector(shakeOff * 0.3f);
                cam.transform.rotation = Quaternion.LookRotation(t.forward, t.up) * Quaternion.Euler(0, 0, Mathf.Clamp(-latG * 1.2f, -2f, 2f));
                targetFov += 6f;
                if (b == null) { }
            }
            else
            {
                bool far = mode == Mode.ChaseFar;
                Vector3 fwd = U.Flat(t.forward);
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 fv = U.Flat(v);
                Vector3 lookT = fv.magnitude > 4f && Vector3.Dot(fv.normalized, fwd) > 0.3f ? Vector3.Slerp(fwd, fv.normalized, 0.3f) : fwd;
                // savrulma gecikmesi (~0.2 sn): kamera dönüşe biraz geç girer → dönüş hissi
                if (lookDir.sqrMagnitude < 0.5f) lookDir = lookT;
                lookDir = Vector3.Slerp(lookDir, lookT, 1f - Mathf.Exp(-dt / 0.2f)).normalized;
                Vector3 look = lookDir;
                float dist = (far ? 9.5f : 6.2f) + sp01 * (far ? 2.5f : 1.6f);
                float height = far ? 3.4f : 2.2f;
                Vector3 side = new Vector3(look.z, 0, -look.x);
                Vector3 desired = t.position - look * dist + Vector3.up * height - side * Mathf.Clamp(latG * 0.6f, -1.2f, 1.2f);   // virajın dışına kayma
                // engellere girmesin
                RaycastHit hit;
                Vector3 pivot = t.position + Vector3.up * 1.5f;
                if (Physics.Linecast(pivot, desired, out hit) && hit.rigidbody == null)
                    desired = hit.point + (pivot - hit.point).normalized * 0.4f;
                float smooth = Mathf.Lerp(0.12f, 0.06f, sp01);
                cam.transform.position = Vector3.SmoothDamp(cam.transform.position, desired, ref vel, smooth, Mathf.Infinity, dt);
                lean = Mathf.Lerp(lean, Mathf.Clamp(-latG * 2f, -3f, 3f), dt * 4f);
                Vector3 focus = t.position + Vector3.up * (far ? 1.4f : 1.2f) + look * 3f;
                cam.transform.rotation = Quaternion.LookRotation(focus - cam.transform.position, Vector3.up) * Quaternion.Euler(0, 0, lean);
                cam.transform.position += cam.transform.TransformVector(shakeOff);
            }
            fov = Mathf.Lerp(fov, targetFov, dt * 4f);
            cam.fieldOfView = fov;
        }
    }
}
