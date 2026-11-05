/* TechZone — nền 3D WebGL theo chuột (concept landing mới 2026-11-05).
   Shader fbm noise: 2 lớp sóng trôi tốc độ khác nhau, parallax theo chuột
   + cuộn trang, click tạo gợn sóng lan ra từ đúng điểm click, hover nút/
   thẻ làm nền sáng hơn. Veil + lưới chấm (div .bg-dots trong _Layout)
   giữ chữ dễ đọc. Tự giảm chất lượng khi máy chậm (giảm pixelRatio),
   dừng render khi ẩn tab, prefers-reduced-motion thì chỉ vẽ 1 khung
   tĩnh, WebGL lỗi thì gỡ canvas và trang dùng nền CSS cũ (không vỡ).
   Dark mode (2026-11-05): uniform uDark đọc theme theo từng khung hình —
   sóng dùng xanh/teal sáng hơn (#60A5FA/#2DD4BF) và alpha cao hơn để vẫn
   thấy trên nền tối, không thành khoảng đen trơn; đổi theme lúc đang chạy
   thì khung sau tự cập nhật, không cần tải lại trang. */
(function () {
    'use strict';

    var canvas = document.getElementById('webgl-bg');
    if (!canvas) return;

    var reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    function fail() {
        if (canvas.parentNode) canvas.parentNode.removeChild(canvas);
        document.documentElement.classList.add('no-webgl');
    }

    var VERT = [
        'attribute vec2 aPos;',
        'void main() { gl_Position = vec4(aPos, 0.0, 1.0); }'
    ].join('\n');

    var FRAG = [
        'precision highp float;',
        'uniform vec2 uRes;',
        'uniform float uTime;',
        'uniform vec2 uMouse;',
        'uniform float uScroll;',
        'uniform float uHover;',
        'uniform float uDark;',
        'uniform vec4 uRipples[8];',
        'float hash(vec2 p) {',
        '  p = fract(p * vec2(123.34, 456.21));',
        '  p += dot(p, p + 45.32);',
        '  return fract(p.x * p.y);',
        '}',
        'float noise(vec2 p) {',
        '  vec2 i = floor(p);',
        '  vec2 f = fract(p);',
        '  vec2 u = f * f * (3.0 - 2.0 * f);',
        '  float a = hash(i);',
        '  float b = hash(i + vec2(1.0, 0.0));',
        '  float c = hash(i + vec2(0.0, 1.0));',
        '  float d = hash(i + vec2(1.0, 1.0));',
        '  return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);',
        '}',
        'float fbm(vec2 p) {',
        '  float v = 0.0;',
        '  float a = 0.5;',
        '  for (int i = 0; i < 4; i++) {',
        '    v += a * noise(p);',
        '    p *= 2.03;',
        '    a *= 0.5;',
        '  }',
        '  return v;',
        '}',
        'void main() {',
        '  vec2 uv = gl_FragCoord.xy / uRes;',
        '  vec2 asp = vec2(uRes.x / uRes.y, 1.0);',
        '  vec2 p = uv * asp;',
        '  vec2 m = (uMouse - 0.5) * 0.14;',
        '  float t = uTime;',
        '  float w1 = fbm(p * 1.6 + vec2(t * 0.05, t * 0.03) + m);',
        '  float w2 = fbm(p * 2.6 + vec2(-t * 0.03, t * 0.04 + uScroll * 0.0004) - m * 0.6);',
        '  float w = w1 * 0.65 + w2 * 0.35;',
        '  vec3 blue = mix(vec3(0.145, 0.388, 0.922), vec3(0.376, 0.647, 0.984), uDark);',
        '  vec3 teal = mix(vec3(0.055, 0.647, 0.643), vec3(0.180, 0.831, 0.749), uDark);',
        '  vec3 col = mix(blue, teal, smoothstep(0.35, 0.75, w2));',
        '  col = mix(col, vec3(1.0), smoothstep(0.72, 0.95, w) * (0.35 + 0.10 * uDark));',
        '  float alpha = smoothstep(0.45, 0.85, w) * mix(0.22, 0.34, uDark) * (1.0 + uHover * 0.6);',
        '  float rip = 0.0;',
        '  for (int i = 0; i < 8; i++) {',
        '    vec4 r = uRipples[i];',
        '    float age = t - r.z;',
        '    if (r.w > 0.001 && age > 0.0 && age < 2.5) {',
        '      float d = distance(p, r.xy * asp);',
        '      float ring = sin((d - age * 0.55) * 28.0) * exp(-d * 3.0) * exp(-age * 1.8);',
        '      rip += ring * r.w;',
        '    }',
        '  }',
        '  alpha += clamp(rip, 0.0, 1.0) * mix(0.12, 0.18, uDark);',
        '  gl_FragColor = vec4(col, clamp(alpha, 0.0, mix(0.45, 0.60, uDark)));',
        '}'
    ].join('\n');

    var gl = null;
    try {
        gl = canvas.getContext('webgl', { alpha: true, antialias: false, depth: false, stencil: false, powerPreference: 'low-power' })
            || canvas.getContext('experimental-webgl', { alpha: true, antialias: false, depth: false, stencil: false });
    } catch (e) {
        gl = null;
    }
    if (!gl) { fail(); return; }

    function compile(type, src) {
        var s = gl.createShader(type);
        gl.shaderSource(s, src);
        gl.compileShader(s);
        if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) { gl.deleteShader(s); return null; }
        return s;
    }

    var vs = compile(gl.VERTEX_SHADER, VERT);
    var fs = compile(gl.FRAGMENT_SHADER, FRAG);
    if (!vs || !fs) { fail(); return; }

    var prog = gl.createProgram();
    gl.attachShader(prog, vs);
    gl.attachShader(prog, fs);
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) { fail(); return; }
    gl.useProgram(prog);

    // Tam giác fullscreen (3 đỉnh phủ kín màn hình, khỏi cần index buffer).
    var buf = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buf);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    var aPos = gl.getAttribLocation(prog, 'aPos');
    gl.enableVertexAttribArray(aPos);
    gl.vertexAttribPointer(aPos, 2, gl.FLOAT, false, 0, 0);

    var U = {
        res: gl.getUniformLocation(prog, 'uRes'),
        time: gl.getUniformLocation(prog, 'uTime'),
        mouse: gl.getUniformLocation(prog, 'uMouse'),
        scroll: gl.getUniformLocation(prog, 'uScroll'),
        hover: gl.getUniformLocation(prog, 'uHover'),
        ripples: gl.getUniformLocation(prog, 'uRipples'),
        dark: gl.getUniformLocation(prog, 'uDark')
    };

    // Mỗi gợn sóng: x, y (tọa độ UV, y hướng lên), thời điểm bắt đầu, độ mạnh.
    var rippleData = new Float32Array(32);
    for (var k = 0; k < 8; k++) rippleData[k * 4 + 2] = -100;
    var rippleIdx = 0;

    var dprCap = Math.min(window.devicePixelRatio || 1, 1.5);
    var dpr = dprCap;
    var mouse = { x: 0.5, y: 0.5, tx: 0.5, ty: 0.5 };
    var hover = 0, hoverTarget = 0;
    var startTime = performance.now();
    var rafId = 0;
    var running = false;
    var frames = 0, lastNow = 0, emaDt = 16;

    function resize() {
        var w = Math.max(1, Math.floor(canvas.clientWidth * dpr));
        var h = Math.max(1, Math.floor(canvas.clientHeight * dpr));
        if (canvas.width !== w || canvas.height !== h) {
            canvas.width = w;
            canvas.height = h;
            gl.viewport(0, 0, w, h);
        }
    }

    function draw(now) {
        var t = (now - startTime) / 1000;
        mouse.x += (mouse.tx - mouse.x) * 0.06;
        mouse.y += (mouse.ty - mouse.y) * 0.06;
        hover += (hoverTarget - hover) * 0.08;
        gl.uniform2f(U.res, canvas.width, canvas.height);
        gl.uniform1f(U.time, t);
        gl.uniform2f(U.mouse, mouse.x, mouse.y);
        gl.uniform1f(U.scroll, window.scrollY || window.pageYOffset || 0);
        gl.uniform1f(U.hover, hover);
        /* Dark mode: đọc theme theo từng khung hình nên bấm toggle đổi theme
           lúc đang chạy thì khung sau tự cập nhật, không cần tải lại trang. */
        gl.uniform1f(U.dark, document.documentElement.getAttribute('data-theme') === 'dark' ? 1 : 0);
        gl.uniform4fv(U.ripples, rippleData);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.drawArrays(gl.TRIANGLES, 0, 3);
    }

    function loop(now) {
        if (!running) return;
        rafId = requestAnimationFrame(loop);
        if (lastNow) {
            var dt = now - lastNow;
            emaDt = emaDt * 0.95 + dt * 0.05;
            frames++;
            // Máy chậm (dưới ~38fps): giảm pixelRatio từng nấc, tối thiểu 0.5.
            if (frames >= 90) {
                frames = 0;
                if (1000 / Math.max(emaDt, 1) < 38 && dpr > 0.5) {
                    dpr = Math.max(0.5, dpr - 0.25);
                    resize();
                }
            }
        }
        lastNow = now;
        draw(now);
    }

    function start() {
        if (running) return;
        running = true;
        lastNow = 0;
        rafId = requestAnimationFrame(loop);
    }

    function stop() {
        running = false;
        if (rafId) cancelAnimationFrame(rafId);
        rafId = 0;
    }

    window.addEventListener('pointermove', function (e) {
        mouse.tx = e.clientX / window.innerWidth;
        mouse.ty = 1 - e.clientY / window.innerHeight;
    }, { passive: true });

    window.addEventListener('pointerdown', function (e) {
        var t = (performance.now() - startTime) / 1000;
        var o = rippleIdx * 4;
        rippleData[o] = e.clientX / window.innerWidth;
        rippleData[o + 1] = 1 - e.clientY / window.innerHeight;
        rippleData[o + 2] = t;
        rippleData[o + 3] = 1;
        rippleIdx = (rippleIdx + 1) % 8;
    }, { passive: true });

    // Hover nút/link/thẻ/input/select thì nền sáng hơn (mượt dần về 1).
    document.addEventListener('pointerover', function (e) {
        var t = e.target;
        hoverTarget = (t && t.closest)
            ? (t.closest('a, button, .product-card, input, select, textarea, .chip, [role="button"]') ? 1 : 0)
            : 0;
    }, { passive: true });

    document.addEventListener('visibilitychange', function () {
        if (document.hidden) stop();
        else if (!reduceMotion) start();
    });
    window.addEventListener('resize', resize);

    // Dark mode: khi reduce-motion (không chạy vòng lặp), toggle theme thì vẽ
    // lại 1 khung tĩnh cho đúng màu; khi đang chạy, draw() đọc theme mỗi khung
    // nên khung sau tự cập nhật, không cần tải lại trang.
    document.addEventListener('techzone:themechange', function () {
        if (reduceMotion && !running) draw(performance.now());
    });

    canvas.addEventListener('webglcontextlost', function (e) {
        e.preventDefault();
        stop();
        fail();
    });

    resize();
    if (reduceMotion) {
        draw(performance.now()); // Chỉ vẽ 1 khung tĩnh, không chạy vòng lặp.
    } else {
        start();
    }
})();
