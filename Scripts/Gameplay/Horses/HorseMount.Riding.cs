using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;
using static WorldUtil;

// HorseMount: Riding (split out of HorseMount.cs; same class, same behaviour)
public partial class HorseMount
{
    // ---------- Riding ----------

    void RideInput(float dt, ref float wantedSpeed, ref float wantedHeading)

    {

        if (mounting || dismounting) return;

        PlayerEquipmentManager gear = PlayerEquipmentManager.Instance;

        bool menuOpen = gear != null && gear.IsOpen;

        if (!menuOpen && Input.GetKeyDown(mountKey)) { Dismount(); return; }

        float h = menuOpen ? 0f : Input.GetAxisRaw("Horizontal");

        float v = menuOpen ? 0f : Input.GetAxisRaw("Vertical");

        // Long hold always falls back to the intermediate gait, irrespective of the previous toggle.
        bool held=sprintInput.Read(Input.GetKey(runKey),Input.GetKeyDown(runKey),Input.GetKeyUp(runKey),
            menuOpen,Time.time,exhausted,ref trotOn);

        if (steering == Steering.KeysTurnTheHorse)

        {

            // A/D turn the horse only while it is moving (W held or still rolling): standing still it does not spin on the spot

            bool moving = v > 0.1f || Mathf.Abs(speed) > 0.3f;

            if (Mathf.Abs(h) > 0.1f && moving) wantedHeading = heading + Mathf.Sign(h) * 90f;

            else if (v > 0.1f && cameraSteer > 0f && Camera.main != null)

            {

                // A mix of the two: the horse slowly bends towards where you look

                float toCamera = Mathf.DeltaAngle(heading, Camera.main.transform.eulerAngles.y);

                if (Mathf.Abs(toCamera) < cameraSteerMaxAngle)

                    wantedHeading = heading + toCamera * (1f - Mathf.Exp(-cameraSteer * 2f * dt));

            }

            if (v > 0.1f)

            {

                galloping = held && !exhausted;

                wantedSpeed = galloping ? gallopSpeed : trotOn ? trotSpeed : walkSpeed;

            }

            else

            {

                galloping = false;

                if (v < -0.1f && speed < 0.3f) wantedSpeed = -backSpeed;

            }

            return;

        }

        Vector3 input = new Vector3(h, 0f, v);

        if (input.sqrMagnitude < 0.01f)

        {

            galloping = false;

            return;

        }

        input.Normalize();

        Transform cam = Camera.main != null ? Camera.main.transform : null;

        float camYaw = cam != null ? cam.eulerAngles.y : heading;

        wantedHeading = camYaw + Mathf.Atan2(input.x, input.z) * Mathf.Rad2Deg;

        galloping = held && !exhausted;

        wantedSpeed = galloping ? gallopSpeed : trotOn ? trotSpeed : walkSpeed;

        // Asked to go the other way: slow down while turning

        float off = Mathf.Abs(Mathf.DeltaAngle(heading, wantedHeading));

        if (off > 60f) wantedSpeed *= Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(60f, 150f, off));

    }

    void UpdateStamina(float dt)

    {

        bool reallyGalloping = galloping && speed > trotSpeed + 0.5f;

        stamina = reallyGalloping

            ? Mathf.Max(0f, stamina - gallopDrain * dt)

            : Mathf.Min(maxStamina, stamina + staminaRegen * dt);

        if (stamina <= 0f)
        {
            exhausted=true;
            if(galloping || Ridden==this){sprintInput.Exhaust(ref trotOn);galloping=false;}
        }

        else if (exhausted && stamina >= maxStamina * gallopAgainAt) exhausted = false;

    }
}
