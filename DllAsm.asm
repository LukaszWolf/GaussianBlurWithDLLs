option casemap:none
.code

; RCX=imagePtr, RDX=width, R8D=stride, R9D=startY, 5-ty = linesForThread (weŸmiemy z R11)
MyProc1 PROC imagePtr:QWORD, imgWidth:DWORD, imgStride:DWORD, startY:DWORD, linesForThread:DWORD

    ; zachowaj nieulotne
    push rbx
    push rsi
    push rdi
    push r12

    ; parametry
    mov     rsi, rcx            ; base ptr
    mov     ebx, edx            ; width
    mov     r10d, r8d           ; stride
    mov     r12d, r9d           ; startY

    ; linesForThread jest teraz 5-tym — pobierz je *raz* poprawnie:
    ; UWAGA: na wejœciu mamy return @ [rsp+0], shadow space @ [rsp+8..+28]
    ; wiêc 5-ty argument le¿y pod [rsp+28h] + 8 = [rsp+30h] *po* naszych pushach.
    ; Zrobiliœmy 4 pushy (=32 bajty), wiêc: 30h + 20h = 50h.
    mov     ecx, DWORD PTR [rsp+50h]    ; ecx = linesForThread

    ; przesuniêcie do wiersza startY
    mov     r11d, r12d                   ; r11 = startY
    imul    r11, r10                     ; r11 = startY * stride (64-bit)
    add     rsi, r11                     ; rsi -> pocz¹tek wiersza startY

    ; kolor
    mov     eax, 0FFAA00AAh

row_loop:
    test    ecx, ecx
    jz      done

    mov     edx, ebx
    mov     rdi, rsi
col_loop:
    test    edx, edx
    jz      next_row
    mov     dword ptr [rdi], eax
    add     rdi, 4
    dec     edx
    jmp     col_loop

next_row:
    add     rsi, r10
    dec     ecx
    jmp     row_loop

done:
    pop     r12
    pop     rdi
    pop     rsi
    pop     rbx
    ret
MyProc1 ENDP
END
