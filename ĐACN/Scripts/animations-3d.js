/**
 * ==========================================================================
 * TAPFOOD 3D & ULTRA ANIMATION ENGINE (animations-3d.js)
 * High-performance 60fps animations, 3D tilt, counter animations,
 * card flips, parallax, skeleton loaders, and particle background.
 * ==========================================================================
 */

(function (window, document) {
    'use strict';

    var TapFoodAnimation = {
        /**
         * Initialize all animations on DOM ready
         */
        init: function () {
            this.initEntranceAnimations();
            this.initTilt3D();
            this.initCounterAnimations();
            this.initCardFlip3D();
            this.initParallax();
            this.initProgressBarAnimations();
            this.initHeroParticles();
        },

        /**
         * 1. 3D Tilt with Specular Glare Effect
         */
        initTilt3D: function () {
            var selector = '.tilt-3d, .order-card, .income-card, .premium-card, .calendar-day, .kpi-card';
            var cards = document.querySelectorAll(selector);

            cards.forEach(function (card) {
                // Never apply tilt to admin cards, tables, or list containers
                if (card.classList.contains('admin-card') ||
                    card.closest('.admin-card') ||
                    card.closest('.admin-main') ||
                    card.querySelector('table') ||
                    card.closest('.table-responsive') ||
                    card.classList.contains('no-tilt')) {
                    return;
                }

                // Ensure card has glare element
                var glare = card.querySelector('.tilt-glare');
                if (!glare) {
                    glare = document.createElement('div');
                    glare.className = 'tilt-glare';
                    card.appendChild(glare);
                }

                card.addEventListener('mousemove', function (e) {
                    var rect = card.getBoundingClientRect();
                    var x = e.clientX - rect.left;
                    var y = e.clientY - rect.top;

                    var centerX = rect.width / 2;
                    var centerY = rect.height / 2;

                    // Calculate tilt angles (max +/- 10 degrees)
                    var rotateX = ((centerY - y) / centerY) * 8;
                    var rotateY = ((x - centerX) / centerX) * 8;

                    card.style.transform = 'perspective(1000px) rotateX(' + rotateX.toFixed(2) + 'deg) rotateY(' + rotateY.toFixed(2) + 'deg) scale3d(1.02, 1.02, 1.02)';

                    // Update glare position
                    var glareX = (x / rect.width) * 100;
                    var glareY = (y / rect.height) * 100;
                    card.style.setProperty('--glare-x', glareX.toFixed(1) + '%');
                    card.style.setProperty('--glare-y', glareY.toFixed(1) + '%');
                    card.style.setProperty('--glare-opacity', '1');
                });

                card.addEventListener('mouseleave', function () {
                    card.style.transform = 'perspective(1000px) rotateX(0deg) rotateY(0deg) scale3d(1, 1, 1)';
                    card.style.setProperty('--glare-opacity', '0');
                });
            });
        },

        /**
         * 2. Smooth Counter Animations (IntersectionObserver triggered)
         */
        initCounterAnimations: function () {
            var self = this;
            // Target elements that typically hold metrics
            var targets = document.querySelectorAll('.stat-number, .counter-val, [data-counter], .kpi-card .fs-4.fw-bold');

            if (!('IntersectionObserver' in window)) {
                targets.forEach(function (el) { self.animateCounter(el); });
                return;
            }

            var observer = new IntersectionObserver(function (entries, obs) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting) {
                        var target = entry.target;
                        if (!target.getAttribute('data-animated')) {
                            self.animateCounter(target);
                            target.setAttribute('data-animated', 'true');
                        }
                        obs.unobserve(target);
                    }
                });
            }, { threshold: 0.2 });

            targets.forEach(function (el) {
                observer.observe(el);
            });
        },

        animateCounter: function (el) {
            var rawText = (el.getAttribute('data-counter') || el.innerText || '').trim();
            if (!rawText) return;

            // Extract numbers and non-numeric suffixes (e.g., "1.500.000 đ", "85%", "+12 đơn")
            var hasCurrency = rawText.indexOf('đ') !== -1 || rawText.indexOf('VND') !== -1 || rawText.indexOf('VNĐ') !== -1;
            var isPercentage = rawText.indexOf('%') !== -1;
            var isPlus = rawText.indexOf('+') !== -1;

            // Clean number (remove non-digits except dot or comma)
            var cleanNumStr = rawText.replace(/[^\d]/g, '');
            if (!cleanNumStr) return;

            var finalValue = parseInt(cleanNumStr, 10);
            if (isNaN(finalValue) || finalValue === 0) return;

            var duration = 1400; // ms
            var startTime = null;

            function easeOutExpo(x) {
                return x === 1 ? 1 : 1 - Math.pow(2, -10 * x);
            }

            function step(timestamp) {
                if (!startTime) startTime = timestamp;
                var progress = Math.min((timestamp - startTime) / duration, 1);
                var easedProgress = easeOutExpo(progress);
                var currentVal = Math.round(easedProgress * finalValue);

                // Format string
                var formatted = currentVal.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ".");
                if (isPlus) formatted = '+' + formatted;
                if (hasCurrency) formatted += ' đ';
                if (isPercentage) formatted += '%';

                el.innerText = formatted;

                if (progress < 1) {
                    window.requestAnimationFrame(step);
                } else {
                    el.classList.add('counter-pop');
                    setTimeout(function () {
                        el.classList.remove('counter-pop');
                    }, 500);
                }
            }

            window.requestAnimationFrame(step);
        },

        /**
         * 3. 3D Card Flip Handler
         */
        initCardFlip3D: function () {
            var flips = document.querySelectorAll('.card-flip-wrap');
            flips.forEach(function (wrap) {
                var flipBtns = wrap.querySelectorAll('[data-flip-btn]');
                flipBtns.forEach(function (btn) {
                    btn.addEventListener('click', function (e) {
                        e.stopPropagation();
                        wrap.classList.toggle('flipped');
                    });
                });
            });
        },

        /**
         * 4. Parallax Scrolling and Floating Elements
         */
        initParallax: function () {
            var heroBanner = document.querySelector('.banner');
            var floatingItems = document.querySelectorAll('.float-3d, .parallax-layer');

            if (heroBanner) {
                window.addEventListener('scroll', function () {
                    var scrolled = window.pageYOffset;
                    if (scrolled < 700) {
                        heroBanner.style.backgroundPositionY = (scrolled * 0.45) + 'px';
                    }
                }, { passive: true });
            }

            if (floatingItems.length > 0) {
                window.addEventListener('mousemove', function (e) {
                    var x = (e.clientX - window.innerWidth / 2) / 35;
                    var y = (e.clientY - window.innerHeight / 2) / 35;

                    floatingItems.forEach(function (item, idx) {
                        var factor = (idx % 2 === 0 ? 1 : -1) * 0.8;
                        item.style.transform = 'translate3d(' + (x * factor) + 'px, ' + (y * factor) + 'px, 0)';
                    });
                });
            }
        },

        /**
         * 5. Animated Progress Bars
         */
        initProgressBarAnimations: function () {
            var bars = document.querySelectorAll('.animated-progress-bar, .progress-bar-custom');

            if (!('IntersectionObserver' in window)) {
                bars.forEach(function (bar) {
                    var targetWidth = bar.getAttribute('data-width') || bar.style.width || '100%';
                    bar.style.width = targetWidth;
                });
                return;
            }

            var observer = new IntersectionObserver(function (entries, obs) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting) {
                        var bar = entry.target;
                        var targetWidth = bar.getAttribute('data-width') || bar.getAttribute('aria-valuenow') + '%' || bar.style.width;
                        bar.style.width = '0%';
                        setTimeout(function () {
                            bar.style.width = targetWidth;
                        }, 50);
                        obs.unobserve(bar);
                    }
                });
            }, { threshold: 0.1 });

            bars.forEach(function (b) { observer.observe(b); });
        },

        /**
         * 6. Page Entrance Stagger Animations
         */
        initEntranceAnimations: function () {
            var cardGrids = document.querySelectorAll('.row > [class*="col-"], .calendar-week > .calendar-day');
            cardGrids.forEach(function (item, index) {
                item.classList.add('animate-entrance');
                var delay = Math.min((index % 8) * 0.06, 0.45);
                item.style.animationDelay = delay + 's';
            });
        },

        /**
         * 7. Particle Background Canvas (Lightweight, 60fps)
         */
        initHeroParticles: function () {
            var hero = document.querySelector('.banner, .admin-header, .navbar-custom-header');
            if (!hero) return;

            var canvas = document.createElement('canvas');
            canvas.className = 'particle-canvas';
            hero.style.position = 'relative';
            hero.insertBefore(canvas, hero.firstChild);

            var ctx = canvas.getContext('2d');
            var width, height;
            var particles = [];
            var particleCount = window.innerWidth < 768 ? 20 : 45;

            function resize() {
                width = canvas.width = hero.offsetWidth;
                height = canvas.height = hero.offsetHeight;
            }
            resize();
            window.addEventListener('resize', resize);

            var colors = ['rgba(255, 90, 43, 0.4)', 'rgba(255, 159, 28, 0.4)', 'rgba(255, 183, 3, 0.3)', 'rgba(255, 255, 255, 0.5)'];

            for (var i = 0; i < particleCount; i++) {
                particles.push({
                    x: Math.random() * width,
                    y: Math.random() * height,
                    radius: Math.random() * 3 + 1.2,
                    color: colors[Math.floor(Math.random() * colors.length)],
                    vx: (Math.random() - 0.5) * 0.6,
                    vy: (Math.random() - 0.5) * 0.6,
                    alpha: Math.random() * 0.6 + 0.2
                });
            }

            function draw() {
                ctx.clearRect(0, 0, width, height);

                for (var i = 0; i < particles.length; i++) {
                    var p = particles[i];
                    p.x += p.vx;
                    p.y += p.vy;

                    if (p.x < 0) p.x = width;
                    if (p.x > width) p.x = 0;
                    if (p.y < 0) p.y = height;
                    if (p.y > height) p.y = 0;

                    ctx.beginPath();
                    ctx.arc(p.x, p.y, p.radius, 0, Math.PI * 2);
                    ctx.fillStyle = p.color;
                    ctx.shadowBlur = 8;
                    ctx.shadowColor = p.color;
                    ctx.fill();
                }

                requestAnimationFrame(draw);
            }
            requestAnimationFrame(draw);
        }
    };

    /**
     * Skeleton Loader Utility - Replace spinners with slick skeleton placeholders
     */
    window.SkeletonLoader = {
        getCardsHtml: function (count) {
            count = count || 3;
            var html = '';
            for (var i = 0; i < count; i++) {
                html += `
                    <div class="skeleton-card animate-entrance stagger-${(i % 5) + 1}">
                        <div class="d-flex align-items-center mb-3">
                            <div class="skeleton-circle skeleton-shimmer me-3"></div>
                            <div style="flex: 1;">
                                <div class="skeleton-title skeleton-shimmer mb-2"></div>
                                <div class="skeleton-line skeleton-shimmer short"></div>
                            </div>
                        </div>
                        <div class="skeleton-line skeleton-shimmer"></div>
                        <div class="skeleton-line skeleton-shimmer" style="width: 80%;"></div>
                        <div class="d-flex justify-content-between mt-3 pt-2 border-top">
                            <div class="skeleton-shimmer" style="width: 30%; height: 24px; border-radius: 20px;"></div>
                            <div class="skeleton-shimmer" style="width: 25%; height: 24px; border-radius: 20px;"></div>
                        </div>
                    </div>
                `;
            }
            return html;
        }
    };

    // Auto-init on DOMContentLoaded
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            TapFoodAnimation.init();
        });
    } else {
        TapFoodAnimation.init();
    }

    window.TapFoodAnimation = TapFoodAnimation;

})(window, document);
