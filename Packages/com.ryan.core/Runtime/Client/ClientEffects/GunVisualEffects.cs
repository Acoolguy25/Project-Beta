using UnityEngine;

namespace RyanAssets.Clients.ClientEffects {
    public static class GunVisualEffects {
        public static void VisualizeBullet(RaycastHit hit, Vector3 origin, ParticleSystem particleSystem = null) {
            if (particleSystem != null) {
                particleSystem.Play();
            }
            VisualizeBullet(hit, origin);
        }

        public static void VisualizeBullet(RaycastHit hit, Vector3 origin) {
            // A hit with no collider is a shot that reached maximum range, which should leave a
            // tracer but no impact spark.
            VisualizeBullet(origin, hit.point, hit.normal, hit.collider != null);
        }

        /// <summary>
        /// Draws one shot from <paramref name="origin"/> to <paramref name="end"/>.
        /// <para>
        /// This point-and-normal form is the one a replicated shot uses: a <see cref="RaycastHit"/>
        /// cannot be sent over the wire, so a remote observer receives the two ends of the trace and
        /// draws exactly what the shooter drew.
        /// </para>
        /// </summary>
        public static void VisualizeBullet(Vector3 origin, Vector3 end, Vector3 normal, bool hasImpact) {
            GameObject lineObj = new GameObject("BulletTrail");
            LineRenderer lr = lineObj.AddComponent<LineRenderer>();

            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = new Color(1f, 0.9f, 0.5f, 1f);
            lr.endColor = new Color(1f, 0.9f, 0.5f, 1f);
            lr.startWidth = 0.02f;
            lr.endWidth = 0.02f;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;
            lr.SetPosition(0, origin);
            lr.SetPosition(1, end);

            if (hasImpact)
                SpawnImpactEffect(end, normal);

            lineObj.layer = LayerMask.NameToLayer("Ignore Raycast");
            // Instant shot: hold for a single short duration then destroy, no fading
            GameObject.Destroy(lineObj, 0.12f);
        }

        private static void SpawnImpactEffect(Vector3 point, Vector3 normal) {
            GameObject spark = new GameObject("ImpactSpark");
            spark.transform.position = point;
            // A degenerate normal (a hit reported with no surface) would make LookRotation throw.
            spark.transform.rotation = normal.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;

            var ps = spark.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.05f;
            main.startSpeed = 2f;
            main.startSize = 0.05f;
            main.loop = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new ParticleSystem.Burst[] {
                new ParticleSystem.Burst(0f, 8)
            });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 15f;

            var renderer = spark.GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default"));

            GameObject.Destroy(spark, main.duration + main.startLifetime.constant);
        }
    }
}
