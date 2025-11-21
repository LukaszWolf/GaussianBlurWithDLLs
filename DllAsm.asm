option casemap:none

.code

GaussianBlur5x5asm PROC FRAME
    push    rbx
    .pushreg rbx
    push    rsi
    .pushreg rsi
    push    rdi
    .pushreg rdi
    push    r12
    .pushreg r12
    push    r13
    .pushreg r13
    push    r14
    .pushreg r14
    push    r15
    .pushreg r15

    sub     rsp, 40h
    .allocstack 40h
    .endprolog
    pxor    xmm7, xmm7

    mov     rsi, rcx                    ; src
    mov     rdi, rdx                    ; dst
    mov     r12d, r8d                   ; width
    mov     r13d, r9d                   ; height

    mov     r14d, dword ptr [rsp+0A0h]  ; stride
    mov     r15d, dword ptr [rsp+0A8h]  ; startY
    mov     ebx,  dword ptr [rsp+0B0h]  ; linesForThread

    ; Walidacja
    test    rsi, rsi
    jz      done
    test    rdi, rdi
    jz      done
    test    r12d, r12d
    jle     done

    ; Oblicz endY
    mov     eax, r15d
    add     eax, ebx
    cmp     eax, r13d
    cmovg   eax, r13d
    mov     ebx, eax

    cmp     r15d, ebx
    jge     done

    ; Przesuñ do startY
    mov     eax, r15d
    imul    eax, r14d
    movsxd  rax, eax
    add     rsi, rax
    add     rdi, rax

    movsxd  r14, r14d

    ; Za³aduj mno¿nik raz na pocz¹tku
    movdqa  xmm6, xmmword ptr [mul090]  ; sta³a 230

align 16
row_loop:
    cmp     r15d, ebx
    jge     done

    xor     edx, edx                    ; x = 0

align 16
pixel_loop:
    cmp     edx, r12d
    jge     next_row

    ; Oblicz offset
    mov     eax, edx
    shl     eax, 2
    movsxd  rcx, eax

    ; Odczyt BGRA (4 bajty)
    movd    xmm0, dword ptr [rsi + rcx]

    ; Rozpakuj do 16-bit: [B,G,R,A,0,0,0,0]
    punpcklbw xmm0, xmm7

    ; POPRAWIONE MNO¯ENIE:
    ; Mno¿ymy przez 230, potem dzielimy przez 256
    pmullw    xmm0, xmm6        ; multiply LOW (zwraca dolne 16 bitów)
    psrlw     xmm0, 8           ; shift right by 8 (dzielenie przez 256)

    ; Pakuj z powrotem do 8-bit
    packuswb  xmm0, xmm0

    ; Zapisz
    movd    dword ptr [rdi + rcx], xmm0

    inc     edx
    jmp     pixel_loop

next_row:
    add     rsi, r14
    add     rdi, r14
    inc     r15d
    jmp     row_loop

done:
    add     rsp, 40h
    pop     r15
    pop     r14
    pop     r13
    pop     r12
    pop     rdi
    pop     rsi
    pop     rbx
    ret

GaussianBlur5x5asm ENDP

.data
align 16
mul090  dw  150,150,150,150, 150,150,150,150   ; 0.9 * 256 = 230

END