using UnityEngine;
using UnityEngine.InputSystem;

namespace Kishiwada
{
    // 操作：キーボード・ゲームパッド（トリガーでアナログの前梃子）・タッチ（HUD のボタンから入る）
    public sealed class GameInput
    {
        public bool tap, jump, camNext, pause, confirm;
        public float maeL, maeR;   // 0..1
        public float rear;         // -1..1（+ で左へ回す）
        public bool rearManual;    // 後梃子を自分で操作したか（しなければ前梃子に合わせて自動）
        public bool usingTouch, usingPad;
        // HUD（タッチ）から
        public bool touchMaeL, touchMaeR; public int touchTaps, touchJumps;
        float kL, kR;

        public void Poll(float dt)
        {
            tap = jump = camNext = pause = confirm = false;
            float mL = 0f, mR = 0f, rr = 0f;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) tap = true;
                bool l = kb.leftArrowKey.isPressed || kb.qKey.isPressed, r = kb.rightArrowKey.isPressed || kb.eKey.isPressed;
                // キーはすっと入れる（0.12 秒で全開）
                kL = Mathf.MoveTowards(kL, l ? 1f : 0f, dt / 0.12f); kR = Mathf.MoveTowards(kR, r ? 1f : 0f, dt / 0.12f);
                mL = Mathf.Max(mL, kL); mR = Mathf.Max(mR, kR);
                if (kb.aKey.isPressed) rr += 1f;
                if (kb.dKey.isPressed) rr -= 1f;
                if (kb.jKey.wasPressedThisFrame) jump = true;
                if (kb.cKey.wasPressedThisFrame) camNext = true;
                if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) pause = true;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) confirm = true;
                if (kb.anyKey.wasPressedThisFrame) { usingTouch = false; usingPad = false; }
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                if (gp.buttonSouth.wasPressedThisFrame || gp.buttonWest.wasPressedThisFrame) tap = true;
                mL = Mathf.Max(mL, gp.leftTrigger.ReadValue()); mR = Mathf.Max(mR, gp.rightTrigger.ReadValue());
                if (gp.leftShoulder.isPressed) mL = 1f; if (gp.rightShoulder.wasPressedThisFrame) camNext = true;
                float sx = gp.leftStick.ReadValue().x;
                if (Mathf.Abs(sx) > 0.15f) rr += -sx;
                if (gp.buttonNorth.wasPressedThisFrame) jump = true;
                if (gp.startButton.wasPressedThisFrame) pause = true;
                if (gp.buttonSouth.wasPressedThisFrame) confirm = true;
                if (gp.wasUpdatedThisFrame && (gp.buttonSouth.isPressed || gp.leftTrigger.ReadValue() > 0.1f || gp.rightTrigger.ReadValue() > 0.1f)) usingPad = true;
            }
            if (touchMaeL) mL = 1f; if (touchMaeR) mR = 1f;
            if (touchTaps > 0) { tap = true; touchTaps = 0; }
            if (touchJumps > 0) { jump = true; touchJumps = 0; }
            maeL = mL; maeR = mR;
            rearManual = Mathf.Abs(rr) > 0.05f;
            // 後梃子を触らない時は、前梃子に合わせて尻を外へ振る
            rear = rearManual ? Mathf.Clamp(rr, -1f, 1f) : Mathf.Clamp((mL - mR) * 0.75f, -1f, 1f);
        }
    }
}
