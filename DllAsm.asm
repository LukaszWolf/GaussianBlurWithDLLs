option casemap:none
.code

; void MyProc1(void* imagePtr, int width, int height, int stride)
MyProc1 PROC imagePtr:QWORD, imgWidth:DWORD, imgHeight:DWORD, imgStride:DWORD

    ; RCX = imagePtr
    ; EDX = width
    ; R8D = height
    ; R9D = stride

    ; --- ZACHOWAJ rejestry, których dotkniemy ---
    push rbx
    push rsi
    push rdi

    mov rsi, rcx            ; rsi = imagePtr
    mov ebx, edx            ; ebx = width
    mov edi, r8d            ; edi = height
    mov r10d, r9d           ; r10d = stride (bytes per row)

    ; przejdŸ do œrodka obrazu
    mov eax, edi
    shr eax, 1              ; height / 2
    imul eax, r10d          ; eax = stride * (height/2)
    add rsi, rax            ; rsi += offset (po³owa w dó³)

    mov eax, ebx
    shr eax, 1              ; width / 2
    imul eax, 4             ; 4 bytes per pixel
    add rsi, rax            ; rsi += offset (po³owa w bok)

    ; ustaw piksel na czerwony (BGRA = FF 00 00 FF)
    mov BYTE PTR [rsi],   0AAh
    mov BYTE PTR [rsi+1], 00h
    mov BYTE PTR [rsi+2], 0AAh
    mov BYTE PTR [rsi+3], 0AAh

    pop rdi
    pop rsi
    pop rbx
    ret

MyProc1 ENDP
END
