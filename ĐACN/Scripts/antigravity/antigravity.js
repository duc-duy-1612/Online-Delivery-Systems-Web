/**
 * Antigravity Animation Engine
 * Inspired by Google Material Design Antigravity & davidpelayo/antigravity-animation
 * Enhanced for TapFood Delivery Platform
 * 
 * Features:
 * - GPU Accelerated Sprite Caching (pre-rendered shapes + shadows)
 * - Spring Physics & Pattern Formation (circle, square, triangle, heart)
 * - Antigravity Particle Floating & Mouse/Touch Repulsion
 * - High-DPI / Retina Display Scaling
 * - TapFood & Google Color Palettes
 * - Auto-attach to containers & celebration burst mode
 */
(function (global) {
    'use strict';

    // Color Palettes
    var PALETTES = {
        tapfood: ['#ff5a2b', '#ff9f1c', '#ffb703', '#2ec4b6', '#e71d36', '#ffffff'],
        google: ['#EA4335', '#FBBC05', '#34A853', '#4285F4', '#E8F0FE', '#DADCE0'],
        gold: ['#d4af37', '#ffd700', '#f39c12', '#f1c40f', '#fff3cd'],
        dark: ['#3a3a3c', '#48484a', '#636366', '#8e8e93', '#ff5a2b'],
        confetti: ['#ff4d4d', '#ffbe0b', '#3a86ff', '#8338ec', '#fb5607', '#06d6a0']
    };

    // Shared Sprite Cache across instances for optimal GPU memory usage
    var spriteCache = {};

    function getCachedSprite(color, shapeType) {
        var key = color + '_' + shapeType;
        if (spriteCache[key]) {
            return spriteCache[key];
        }

        var size = 64;
        var center = size / 2;
        var drawSize = 28;

        var offscreen = document.createElement('canvas');
        offscreen.width = size;
        offscreen.height = size;
        var cx = offscreen.getContext('2d');

        // Baked Drop Shadow
        cx.shadowColor = 'rgba(0, 0, 0, 0.12)';
        cx.shadowBlur = 12;
        cx.shadowOffsetX = 3;
        cx.shadowOffsetY = 4;
        cx.fillStyle = color;

        cx.translate(center, center);
        cx.beginPath();

        if (shapeType === 0) {
            // Circle
            cx.arc(0, 0, drawSize / 2, 0, Math.PI * 2);
        } else if (shapeType === 1) {
            // Square (slightly rounded corners)
            var half = drawSize / 2;
            var radius = 4;
            cx.moveTo(-half + radius, -half);
            cx.lineTo(half - radius, -half);
            cx.quadraticCurveTo(half, -half, half, -half + radius);
            cx.lineTo(half, half - radius);
            cx.quadraticCurveTo(half, half, half - radius, half);
            cx.lineTo(-half + radius, half);
            cx.quadraticCurveTo(-half, half, -half, half - radius);
            cx.lineTo(-half, -half + radius);
            cx.quadraticCurveTo(-half, -half, -half + radius, -half);
            cx.closePath();
        } else if (shapeType === 2) {
            // Triangle
            cx.moveTo(0, -drawSize / 2);
            cx.lineTo(drawSize / 2, drawSize / 2);
            cx.lineTo(-drawSize / 2, drawSize / 2);
            cx.closePath();
        }

        cx.fill();
        spriteCache[key] = offscreen;
        return offscreen;
    }

    /**
     * Particle Class
     */
    function Particle(engine, index) {
        this.engine = engine;
        this.index = index;
        this.init(true);
    }

    Particle.prototype.init = function (randomY) {
        var e = this.engine;
        var w = e.width || 800;
        var h = e.height || 600;

        this.x = Math.random() * w;
        if (randomY) {
            this.y = Math.random() * h;
        } else {
            this.y = e.options.gravity < 0 ? h + 40 : -40;
        }

        this.visualSize = Math.random() * (e.options.maxSize - e.options.minSize) + e.options.minSize;
        this.vx = (Math.random() - 0.5) * 2 * e.options.speed;
        this.vy = (Math.random() - 0.5) * 2 * e.options.speed;

        if (!e.options.pattern) {
            if (e.options.gravity < 0) {
                this.vy -= Math.random() * Math.abs(e.options.gravity) * 2;
            } else if (e.options.gravity > 0) {
                this.vy += Math.random() * e.options.gravity * 2;
            }
        } else if (!randomY) {
            this.x = w / 2;
            this.y = h / 2;
        }

        var colors = e.options.colors;
        this.color = colors[Math.floor(Math.random() * colors.length)];
        this.rotation = Math.random() * Math.PI * 2;
        this.rotationSpeed = (Math.random() - 0.5) * 0.04;
        this.depth = Math.random() * 0.8 + 0.6; // 0.6 to 1.4

        this.type = this.getShapeType();
        this.sprite = getCachedSprite(this.color, this.type);
    };

    Particle.prototype.getShapeType = function () {
        var s = this.engine.options.shape;
        if (s === 'circle') return 0;
        if (s === 'square') return 1;
        if (s === 'triangle') return 2;
        return Math.floor(Math.random() * 3);
    };

    Particle.prototype.getPatternTarget = function () {
        var e = this.engine;
        var cx = e.width / 2;
        var cy = e.height / 2;
        var r = Math.min(e.width, e.height) * 0.32;
        var total = e.options.particleCount;
        var t = this.index / total;
        var pType = e.options.pattern;

        if (pType === true || pType === 'circle') {
            var angle = t * Math.PI * 2 - Math.PI / 2;
            return { x: cx + Math.cos(angle) * r, y: cy + Math.sin(angle) * r };
        } else if (pType === 'square') {
            var side = Math.floor(t * 4);
            var subT = (t * 4) % 1;
            switch (side) {
                case 0: return { x: cx - r + (2 * r * subT), y: cy - r };
                case 1: return { x: cx + r, y: cy - r + (2 * r * subT) };
                case 2: return { x: cx + r - (2 * r * subT), y: cy + r };
                default: return { x: cx - r, y: cy + r - (2 * r * subT) };
            }
        } else if (pType === 'triangle') {
            var sides = 3;
            var sIdx = Math.floor(t * sides);
            var sSub = (t * sides) % 1;
            var angles = [-Math.PI / 2, -Math.PI / 2 + (Math.PI * 2 / 3), -Math.PI / 2 + (4 * Math.PI / 3)];
            var a1 = angles[sIdx];
            var a2 = angles[(sIdx + 1) % 3];
            var x1 = cx + Math.cos(a1) * r;
            var y1 = cy + Math.sin(a1) * r;
            var x2 = cx + Math.cos(a2) * r;
            var y2 = cy + Math.sin(a2) * r;
            return { x: x1 + (x2 - x1) * sSub, y: y1 + (y2 - y1) * sSub };
        }
        return { x: cx, y: cy };
    };

    Particle.prototype.update = function (mouseX, mouseY) {
        var e = this.engine;

        if (e.options.pattern) {
            var target = this.getPatternTarget();
            var dx = target.x - this.x;
            var dy = target.y - this.y;
            this.vx += dx * 0.0035;
            this.vy += dy * 0.0035;
            this.vx += (Math.random() - 0.5) * 0.06;
            this.vy += (Math.random() - 0.5) * 0.06;
        } else {
            this.vy += e.options.gravity * 0.05 * this.depth;
        }

        this.x += this.vx * this.depth;
        this.y += this.vy * this.depth;
        this.rotation += this.rotationSpeed;

        // Mouse Repulsion
        if (e.options.interactive && mouseX >= 0 && mouseY >= 0) {
            var mdx = this.x - mouseX;
            var mdy = this.y - mouseY;
            var dist = Math.sqrt(mdx * mdx + mdy * mdy);
            var radius = e.options.interactionRadius;

            if (dist < radius && dist > 0.001) {
                var force = (radius - dist) / radius;
                var angle = Math.atan2(mdy, mdx);
                var push = force * e.options.repelForce;
                this.vx += Math.cos(angle) * push;
                this.vy += Math.sin(angle) * push;
            }
        }

        // Friction
        this.vx *= e.options.friction;
        this.vy *= e.options.friction;

        // Boundaries
        if (!e.options.pattern) {
            var w = e.width;
            var h = e.height;
            if (this.x < -60) this.x = w + 60;
            if (this.x > w + 60) this.x = -60;

            if (e.options.gravity < 0) {
                if (this.y < -70) this.init(false);
            } else if (e.options.gravity > 0) {
                if (this.y > h + 70) this.init(false);
            } else {
                if (this.y < -70) this.y = h + 70;
                if (this.y > h + 70) this.y = -70;
            }
        }
    };

    Particle.prototype.draw = function (ctx) {
        ctx.save();
        ctx.translate(this.x, this.y);
        ctx.rotate(this.rotation);

        var scaleFactor = (this.visualSize * this.depth) / 28;
        var renderSize = 64 * scaleFactor;

        ctx.drawImage(this.sprite, -renderSize / 2, -renderSize / 2, renderSize, renderSize);
        ctx.restore();
    };

    /**
     * Main Antigravity Class
     */
    function Antigravity(target, options) {
        this.options = Object.assign({
            particleCount: 50,
            speed: 0.8,
            gravity: -0.05,       // Negative = upwards (antigravity), Positive = downwards
            shape: 'mixed',       // 'mixed', 'circle', 'square', 'triangle'
            palette: 'tapfood',   // 'tapfood', 'google', 'gold', 'dark', 'confetti'
            colors: null,         // Array of hex strings if custom
            minSize: 6,
            maxSize: 18,
            friction: 0.96,
            interactionRadius: 160,
            repelForce: 3.5,
            interactive: true,
            pattern: false,       // false, true ('circle'), 'square', 'triangle'
            opacity: 1.0,
            zIndex: 0
        }, options || {});

        // Resolve colors
        if (!this.options.colors || !this.options.colors.length) {
            this.options.colors = PALETTES[this.options.palette] || PALETTES.tapfood;
        }

        this._resolveCanvas(target);
        this.particles = [];
        this.running = false;
        this.mouseX = -1000;
        this.mouseY = -1000;
        this._bindEvents();
        this.initParticles();
        this.start();
    }

    Antigravity.prototype._resolveCanvas = function (target) {
        if (typeof target === 'string') {
            var el = document.querySelector(target);
            if (!el) {
                throw new Error('[Antigravity] Target not found: ' + target);
            }
            target = el;
        }

        if (target instanceof HTMLCanvasElement) {
            this.canvas = target;
            this.container = target.parentElement;
        } else if (target instanceof HTMLElement) {
            this.container = target;
            var existing = target.querySelector('canvas.antigravity-canvas');
            if (existing) {
                this.canvas = existing;
            } else {
                this.canvas = document.createElement('canvas');
                this.canvas.className = 'antigravity-canvas';
                this.canvas.style.position = 'absolute';
                this.canvas.style.top = '0';
                this.canvas.style.left = '0';
                this.canvas.style.width = '100%';
                this.canvas.style.height = '100%';
                this.canvas.style.pointerEvents = 'none';
                this.canvas.style.zIndex = this.options.zIndex;
                if (window.getComputedStyle(this.container).position === 'static') {
                    this.container.style.position = 'relative';
                }
                this.container.insertBefore(this.canvas, this.container.firstChild);
            }
        } else {
            throw new Error('[Antigravity] Invalid target specified');
        }

        this.ctx = this.canvas.getContext('2d', {
            alpha: true,
            desynchronized: true,
            willReadFrequently: false
        });

        if (this.options.opacity < 1.0) {
            this.canvas.style.opacity = this.options.opacity;
        }

        this.resize();
    };

    Antigravity.prototype.resize = function () {
        if (!this.canvas) return;
        var rect = this.container ? this.container.getBoundingClientRect() : this.canvas.getBoundingClientRect();
        this.width = rect.width || window.innerWidth;
        this.height = rect.height || window.innerHeight;

        var dpr = window.devicePixelRatio || 1;
        this.canvas.width = this.width * dpr;
        this.canvas.height = this.height * dpr;
        this.ctx.setTransform(1, 0, 0, 1, 0, 0);
        this.ctx.scale(dpr, dpr);
        this.ctx.imageSmoothingEnabled = true;
        this.ctx.imageSmoothingQuality = 'high';
    };

    Antigravity.prototype.initParticles = function () {
        this.particles = [];
        for (var i = 0; i < this.options.particleCount; i++) {
            this.particles.push(new Particle(this, i));
        }
    };

    Antigravity.prototype._bindEvents = function () {
        var self = this;
        this._onResize = function () { self.resize(); };
        window.addEventListener('resize', this._onResize);

        var listenTarget = this.container || window;

        this._onMouseMove = function (e) {
            var rect = self.canvas.getBoundingClientRect();
            self.mouseX = e.clientX - rect.left;
            self.mouseY = e.clientY - rect.top;
        };

        this._onMouseLeave = function () {
            self.mouseX = -1000;
            self.mouseY = -1000;
        };

        this._onTouchMove = function (e) {
            if (e.touches && e.touches.length) {
                var rect = self.canvas.getBoundingClientRect();
                self.mouseX = e.touches[0].clientX - rect.left;
                self.mouseY = e.touches[0].clientY - rect.top;
            }
        };

        listenTarget.addEventListener('mousemove', this._onMouseMove);
        listenTarget.addEventListener('mouseleave', this._onMouseLeave);
        listenTarget.addEventListener('touchmove', this._onTouchMove, { passive: true });
        listenTarget.addEventListener('touchend', this._onMouseLeave);
    };

    Antigravity.prototype.start = function () {
        if (this.running) return;
        this.running = true;
        var self = this;

        function loop() {
            if (!self.running) return;
            self.ctx.clearRect(0, 0, self.width, self.height);

            var len = self.particles.length;
            for (var i = 0; i < len; i++) {
                var p = self.particles[i];
                p.update(self.mouseX, self.mouseY);
                p.draw(self.ctx);
            }

            self.animationFrame = requestAnimationFrame(loop);
        }

        this.animationFrame = requestAnimationFrame(loop);
    };

    Antigravity.prototype.pause = function () {
        this.running = false;
        if (this.animationFrame) {
            cancelAnimationFrame(this.animationFrame);
        }
    };

    Antigravity.prototype.setPattern = function (pattern) {
        this.options.pattern = pattern;
    };

    Antigravity.prototype.setGravity = function (val) {
        this.options.gravity = parseFloat(val);
    };

    Antigravity.prototype.setPalette = function (name) {
        this.options.palette = name;
        this.options.colors = PALETTES[name] || PALETTES.tapfood;
        this.initParticles();
    };

    Antigravity.prototype.explode = function (x, y, power) {
        x = x !== undefined ? x : this.width / 2;
        y = y !== undefined ? y : this.height / 2;
        power = power || 12;

        for (var i = 0; i < this.particles.length; i++) {
            var p = this.particles[i];
            var dx = p.x - x;
            var dy = p.y - y;
            var dist = Math.sqrt(dx * dx + dy * dy) || 1;
            var angle = Math.atan2(dy, dx);
            var force = (1 / dist) * 100 * power;
            p.vx += Math.cos(angle) * force;
            p.vy += Math.sin(angle) * force;
        }
    };

    Antigravity.prototype.destroy = function () {
        this.pause();
        window.removeEventListener('resize', this._onResize);
        var listenTarget = this.container || window;
        listenTarget.removeEventListener('mousemove', this._onMouseMove);
        listenTarget.removeEventListener('mouseleave', this._onMouseLeave);
        listenTarget.removeEventListener('touchmove', this._onTouchMove);
        listenTarget.removeEventListener('touchend', this._onMouseLeave);
        if (this.canvas && this.canvas.parentElement) {
            this.canvas.parentElement.removeChild(this.canvas);
        }
    };

    /**
     * Static Celebration Helper
     */
    Antigravity.celebrate = function (options) {
        options = Object.assign({
            duration: 3500,
            particleCount: 80,
            gravity: 0.15,
            speed: 2.2,
            palette: 'confetti',
            zIndex: 99999
        }, options || {});

        var overlay = document.createElement('div');
        overlay.style.position = 'fixed';
        overlay.style.top = '0';
        overlay.style.left = '0';
        overlay.style.width = '100vw';
        overlay.style.height = '100vh';
        overlay.style.pointerEvents = 'none';
        overlay.style.zIndex = options.zIndex;
        document.body.appendChild(overlay);

        var instance = new Antigravity(overlay, options);
        instance.explode(window.innerWidth / 2, window.innerHeight / 3, 20);

        setTimeout(function () {
            overlay.style.transition = 'opacity 0.8s ease';
            overlay.style.opacity = '0';
            setTimeout(function () {
                instance.destroy();
                if (overlay.parentElement) overlay.parentElement.removeChild(overlay);
            }, 800);
        }, options.duration);

        return instance;
    };

    /**
     * Auto initialize on elements with data-antigravity
     */
    Antigravity.initAuto = function () {
        var elements = document.querySelectorAll('[data-antigravity]');
        elements.forEach(function (el) {
            if (el._antigravityInstance) return;
            var opts = {};
            if (el.dataset.antigravityPalette) opts.palette = el.dataset.antigravityPalette;
            if (el.dataset.antigravityCount) opts.particleCount = parseInt(el.dataset.antigravityCount, 10);
            if (el.dataset.antigravityGravity) opts.gravity = parseFloat(el.dataset.antigravityGravity);
            if (el.dataset.antigravityPattern) opts.pattern = el.dataset.antigravityPattern;
            if (el.dataset.antigravityShape) opts.shape = el.dataset.antigravityShape;
            if (el.dataset.antigravityOpacity) opts.opacity = parseFloat(el.dataset.antigravityOpacity);
            el._antigravityInstance = new Antigravity(el, opts);
        });
    };

    // Auto init on DOM ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', Antigravity.initAuto);
    } else {
        Antigravity.initAuto();
    }

    // Expose to global namespace
    global.Antigravity = Antigravity;
    global.AntigravityPalettes = PALETTES;

})(typeof window !== 'undefined' ? window : this);
