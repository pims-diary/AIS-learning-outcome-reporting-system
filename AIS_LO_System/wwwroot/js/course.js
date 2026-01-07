const headers = document.querySelectorAll(".accordion-header");
const toggleAllBtn = document.getElementById("toggleAll");

headers.forEach(header => {
    header.addEventListener("click", () => {
        const accordion = header.parentElement;
        accordion.classList.toggle("active");
    });
});

toggleAllBtn.addEventListener("click", () => {
    const accordions = document.querySelectorAll(".accordion");
    const expanding = toggleAllBtn.innerText === "Expand all";

    accordions.forEach(acc => {
        acc.classList.toggle("active", expanding);
    });

    toggleAllBtn.innerText = expanding ? "Collapse all" : "Expand all";
});
