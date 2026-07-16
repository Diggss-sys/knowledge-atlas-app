// Constants for uniform motion as dictated by the design system
const EASE = "power3.inOut";
const DURATION = { fast: 0.3, base: 0.8, slow: 1.2 };

document.addEventListener("DOMContentLoaded", () => {
  // 1. Cinematic Intro Loader
  const counter = document.querySelector('.loader-count');
  const overlay = document.querySelector('.loader-overlay');
  let progress = 0;
  
  // Fake the loading progress so it hits 100% in ~1.5s
  const progressCircle = document.querySelector('.loader-progress');
  const circumference = 339.29;
  
  const interval = setInterval(() => {
    progress += Math.random() * 8;
    if (progress >= 100) {
      progress = 100;
      clearInterval(interval);
      
      // Wipe the overlay away
      gsap.to(overlay, {
        yPercent: -100, 
        duration: DURATION.slow, 
        ease: EASE,
        delay: 0.2,
        onComplete: initPage
      });
    }
    if (counter) counter.textContent = Math.floor(progress) + '%';
    
    // Animate the SVG stroke
    if (progressCircle) {
      const offset = circumference - (progress / 100) * circumference;
      gsap.to(progressCircle, { strokeDashoffset: offset, duration: 0.1, ease: "none" });
    }
  }, 80);

  function initPage() {
    // 1. Reveal header and nav
    gsap.fromTo(".gsap-reveal:not(.pin-section)", 
      { y: 40, opacity: 0 },
      { y: 0, opacity: 1, duration: DURATION.base, stagger: 0.1, ease: EASE }
    );
    
    // 2. Reveal all sections immediately
    const sections = document.querySelectorAll(".pin-section");
    sections.forEach(section => {
      gsap.fromTo(section.querySelectorAll("h2, p, .callout, table, ul, h3"),
        { y: 30, opacity: 0 },
        { y: 0, opacity: 1, stagger: 0.1, duration: DURATION.base, ease: EASE, delay: 0.2 }
      );
    });

    // Subte hover effects on nav
    const navLinks = document.querySelectorAll(".side-nav a");
    navLinks.forEach(link => {
      link.addEventListener("mouseenter", () => {
        if(!link.classList.contains("active")) {
          gsap.to(link, { x: 5, duration: DURATION.fast, ease: EASE });
        }
      });
      link.addEventListener("mouseleave", () => {
        if(!link.classList.contains("active")) {
          gsap.to(link, { x: 0, duration: DURATION.fast, ease: EASE });
        }
      });
    });
  }
});
