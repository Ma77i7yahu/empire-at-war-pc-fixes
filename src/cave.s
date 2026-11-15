/* EaW / FoC camera: Q/E rotate, Z/C tilt (Force Commander style).
   Hooked inside camera helper Update(this=rdi, dt=xmm7), at the
   "cmp byte ptr [rdi+0x80], sil / je" before the scripted-rotation step.
   Placeholders (patched by build.py):
     0x11111111 = GetKeyState IAT      0x22222222 = static camera constants
     0x33333333 = je target            0x44444444 = return address
     VCALL_GETCTRL = camera vtable offset returning the TacticalCameraController */
    .intel_syntax noprefix
    .text
cave:
    jmp     start
    /* exit block first so the early-outs can use short jumps (FoC slack is tight) */
done:
    add     rsp, 0x40
    /* replaced instructions */
    cmp     byte ptr [rdi + 0x80], sil
    .byte 0x0f, 0x84
    .long 0x33333333
    .byte 0xe9
    .long 0x44444444

start:
    sub     rsp, 0x40                      /* rsp is 16-aligned at the hook */
    mov     rcx, [rdi + 0x30]
    test    rcx, rcx
    jz      done
    cmp     byte ptr [rdi], 0
    je      done
    mov     rax, [rcx]
    call    qword ptr [rax + VCALL_GETCTRL]
    test    rax, rax
    jz      done
    mov     [rsp + 0x30], rax
    xor     eax, eax
    mov     [rsp + 0x38], rax              /* +0x38 yaw dir, +0x3c pitch dir */

    mov     ecx, 0x51                      /* Q rotate left */
    call    qword ptr [rip + 0x11111111]
    test    ax, ax
    jns     1f
    dec     dword ptr [rsp + 0x38]
1:  mov     ecx, 0x45                      /* E rotate right */
    call    qword ptr [rip + 0x11111111]
    test    ax, ax
    jns     2f
    inc     dword ptr [rsp + 0x38]
2:  mov     ecx, 0x43                      /* C tilt down (flatter) */
    call    qword ptr [rip + 0x11111111]
    test    ax, ax
    jns     3f
    dec     dword ptr [rsp + 0x3c]
3:  mov     ecx, 0x5A                      /* Z tilt up (more top-down) */
    call    qword ptr [rip + 0x11111111]
    test    ax, ax
    jns     4f
    inc     dword ptr [rsp + 0x3c]
4:
    mov     rax, [rsp + 0x38]
    test    rax, rax
    jz      done

    mov     rcx, [rsp + 0x30]
    lea     rdx, [rip + 0x22222222]
    cmp     byte ptr [rcx + 0x10e], 0
    je      5f
    mov     rdx, [rcx + 0x148]
5:
    /* yaw += dir * rate * dt, wrapped to [0,360), clamped to Yaw_Min/Max */
    cvtsi2ss xmm0, dword ptr [rsp + 0x38]
    mulss   xmm0, [rip + yaw_rate]
    mulss   xmm0, xmm7
    addss   xmm0, [rcx + 0x14]
    movss   xmm1, [rip + f360]
    xorps   xmm2, xmm2
6:  comiss  xmm0, xmm2
    jae     7f
    addss   xmm0, xmm1
    jmp     6b
7:  comiss  xmm0, xmm1
    jb      8f
    subss   xmm0, xmm1
    jmp     7b
8:  maxss   xmm0, [rdx + 0x20]
    minss   xmm0, [rdx + 0x24]
    movss   [rcx + 0x14], xmm0

    /* pitch += dir * rate * dt, clamped to Pitch_Min/Max */
    cvtsi2ss xmm0, dword ptr [rsp + 0x3c]
    mulss   xmm0, [rip + pitch_rate]
    mulss   xmm0, xmm7
    addss   xmm0, [rcx + 0x10]
    maxss   xmm0, [rdx + 4]
    minss   xmm0, [rdx + 8]
    movss   [rcx + 0x10], xmm0
    jmp     done

    .balign 4
yaw_rate:   .float 90.0     /* degrees per second */
pitch_rate: .float 45.0
f360:       .float 360.0
