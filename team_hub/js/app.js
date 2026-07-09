document.addEventListener("DOMContentLoaded", (event) => {
  // Ensure GSAP is loaded
  if (typeof gsap !== "undefined") {
    
    // Smooth stagger reveal for all main elements
    gsap.fromTo(".gsap-reveal", 
      { 
        y: 30, 
        opacity: 0,
        visibility: "hidden"
      },
      {
        y: 0,
        opacity: 1,
        visibility: "visible",
        duration: 0.8,
        stagger: 0.15,
        ease: "power2.out"
      }
    );

    // Add subtle hover effects to side-nav using GSAP
    const navLinks = document.querySelectorAll(".side-nav a");
    navLinks.forEach(link => {
      link.addEventListener("mouseenter", () => {
        if(!link.classList.contains("active")) {
          gsap.to(link, { x: 5, duration: 0.2, ease: "power1.out" });
        }
      });
      link.addEventListener("mouseleave", () => {
        if(!link.classList.contains("active")) {
          gsap.to(link, { x: 0, duration: 0.2, ease: "power1.in" });
        }
      });
    });

  } else {
    console.warn("GSAP is not loaded. Animations will not play.");
    // Fallback to make elements visible if GSAP fails
    document.querySelectorAll(".gsap-reveal").forEach(el => {
      el.style.opacity = 1;
      el.style.visibility = "visible";
    });
  }
});
