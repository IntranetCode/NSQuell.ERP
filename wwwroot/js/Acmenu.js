document.addEventListener("DOMContentLoaded", function () {
    // User dropdown toggle
    const userToggle = document.getElementById('userToggle');
    const userDropdown = document.getElementById('userDropdown');
    if (userToggle && userDropdown) {
        userToggle.addEventListener('click', function (e) {
            e.stopPropagation();
            userDropdown.classList.toggle('show');
        });

        document.addEventListener('click', function () {
            userDropdown.classList.remove('show');
        });
    }

    // Mobile sidebar toggle
    const mobileToggle = document.getElementById('mobileToggle');
    const sidebar = document.getElementById('sidebar');
    if (mobileToggle && sidebar) {
        mobileToggle.addEventListener('click', function () {
            sidebar.classList.toggle('active');
        });
    }

    // Inicializa submenús ocultos
    document.querySelectorAll('.submenu').forEach(function (submenu) {
        submenu.style.display = 'none';
    });

    // Manejo de clic en el menú
    document.querySelectorAll('.menu-link').forEach(function (link) {
        link.addEventListener('click', function (e) {
            const submenu = this.nextElementSibling;

            // Si tiene submenú, prevenir navegación y hacer toggle
            if (submenu && submenu.classList.contains('submenu')) {
                e.preventDefault();

                // Oculta todos los demás submenús
                document.querySelectorAll('.submenu').forEach(function (sm) {
                    if (sm !== submenu) sm.style.display = 'none';
                });

                // Alterna el submenú clicado
                submenu.style.display = submenu.style.display === 'block' ? 'none' : 'block';
            }

            // Efecto visual: marcar activo
            document.querySelectorAll('.menu-link').forEach(function (l) {
                l.classList.remove('active');
            });
            this.classList.add('active');
        });
    });
});
