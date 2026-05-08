# syntax=docker/dockerfile:1
FROM fedora:44

ARG GM_HOSTNAME=dotnet

ARG GM_RDP_DOCKER_PORT=3389
ARG GM_RDP_USER_UID=1000
ARG GM_RDP_USER_GID=1000
ARG GM_RDP_USER=dev

ARG GM_TIMEZONE=Europe/Budapest
ARG GM_LOCALES=hu_HU.UTF-8,en_US.UTF-8,de_DE.UTF-8
ARG GM_LOCALE=hu_HU.UTF-8
ARG GM_LANGUAGE=hu_HU
ARG GM_KEYMAP=hu
ARG GM_KEYMODEL=pc105

STOPSIGNAL SIGRTMIN+3

RUN --mount=type=cache,id=dnf-bootstrap,target=/var/cache/dnf,sharing=locked \
    sed -i 's/^%_install_langs .*/%_install_langs all/' /etc/rpm/macros.image-language-conf && \
    dnf upgrade -y && \
    dnf install -y \
        glibc \
        tzdata \
        ca-certificates \
        openssl \
        sed

RUN langs=$(echo "$GM_LOCALES" | tr ',' '\n' | sed 's/_.*//' | sort -u | tr '\n' ' ') && \
    pkgs=$(for l in $langs; do printf 'glibc-langpack-%s ' "$l"; done) && \
    dnf install -y $pkgs && \
    echo "LANG=${GM_LOCALE}" > /etc/locale.conf && \
    echo "LC_ALL=${GM_LOCALE}" >> /etc/locale.conf && \
    echo "LANGUAGE=${GM_LANGUAGE}" >> /etc/locale.conf && \
    ln -sf /usr/share/zoneinfo/${GM_TIMEZONE} /etc/localtime && \
    echo "${GM_TIMEZONE}" > /etc/timezone && \
    echo "${GM_HOSTNAME}" > /etc/hostname && \
    echo "KEYMAP=${GM_KEYMAP}" > /etc/vconsole.conf && \
    mkdir -p /etc/X11/xorg.conf.d && \
    printf 'Section "InputClass"\n\tIdentifier "system-keyboard"\n\tMatchIsKeyboard "on"\n\tOption "XkbLayout" "%s"\n\tOption "XkbModel" "%s"\nEndSection\n' \
        "${GM_KEYMAP}" "${GM_KEYMODEL}" > /etc/X11/xorg.conf.d/00-keyboard.conf

ENV TIMEZONE=${GM_TIMEZONE}
ENV LC_ALL=${GM_LOCALE}
ENV LANGUAGE=${GM_LANGUAGE}
ENV LANG=${GM_LOCALE}
ENV GM_RDP_DOCKER_PORT=${GM_RDP_DOCKER_PORT}
ENV GM_RDP_USER=${GM_RDP_USER}
ENV GM_RDP_USER_UID=${GM_RDP_USER_UID}

RUN --mount=type=cache,id=dnf-base,target=/var/cache/dnf,sharing=locked \
    dnf install -y \
        langpacks-hu \
        langpacks-en \
        langpacks-de \
        systemd \
        systemd-pam \
        bash \
        bash-completion \
        ncurses \
        libnotify \
        vte291 \
        coreutils \
        dbus \
        dbus-tools \
        dbus-broker \
        gnome-shell \
        gnome-shell-common \
        gnome-session \
        gnome-settings-daemon \
        gnome-remote-desktop \
        pipewire \
        pipewire-pulseaudio \
        wireplumber \
        mesa-vulkan-drivers \
        mesa-dri-drivers \
        gsettings-desktop-schemas \
        dconf \
        fuse3 \
        sudo \
        procps-ng \
        iproute \
        xdg-desktop-portal \
        xdg-desktop-portal-gnome \
        xdg-desktop-portal-gtk \
        xdg-user-dirs \
        at-spi2-core \
        polkit \
        gnome-keyring \
        gnome-keyring-pam \
        jq \
        libsecret \
        libva \
        krb5-libs \
        tpm2-tss \
        vulkan-loader

RUN --mount=type=cache,id=dnf-extra,target=/var/cache/dnf,sharing=locked \
    dnf install -y \
        gnome-control-center \
        fastfetch \
        gnome-shell-extension-common \
        gnome-tweaks \
        ptyxis \
        curl \
        tree \
        bzip2 \
        flatpak \
        gnome-software \
        gnome-software-fedora-langpacks \
        gnome-user-share \
        geoclue2 \
        evolution-data-server \
        bolt \
        gnome-tweaks gnome-system-monitor \
        nano \
        file \
        dialog \
        papirus-icon-theme \
        webkitgtk6.0 \
        libadwaita \
        ibus \
        ibus-libs \
        ibus-gtk3 \
        ibus-gtk4 \
        mesa-libEGL \
        dotnet-sdk-10.0 && \
    dnf clean all

RUN systemctl mask \
        getty@tty1.service \
        systemd-firstboot.service \
        systemd-udevd.service \
        dnf5-makecache.service || true

RUN groupadd --gid ${GM_RDP_USER_GID} ${GM_RDP_USER} && \
    useradd \
        --uid ${GM_RDP_USER_UID} \
        --gid ${GM_RDP_USER_GID} \
        --create-home \
        --home-dir /home/${GM_RDP_USER} \
        --shell /bin/bash \
        ${GM_RDP_USER} && \
    usermod -aG wheel,audio,video ${GM_RDP_USER}

RUN echo '%wheel ALL=(ALL:ALL) NOPASSWD:ALL' > /etc/sudoers.d/wheel-nopasswd && \
    chmod 440 /etc/sudoers.d/wheel-nopasswd

RUN mkdir -p /var/lib/systemd/linger && \
    touch /var/lib/systemd/linger/${GM_RDP_USER}

USER ${GM_RDP_USER}
WORKDIR /home/${GM_RDP_USER}

# Copy systemd user service files
RUN mkdir -p /home/${GM_RDP_USER}/.config/environment.d && \
    mkdir -p /home/${GM_RDP_USER}/.config/systemd/user/gnome-remote-desktop-headless.service.d && \
    mkdir -p /home/${GM_RDP_USER}/.config/systemd/user/org.gnome.Shell@user.service.d

COPY .user/.config/environment.d/gnome.conf /home/${GM_RDP_USER}/.config/environment.d/gnome.conf

COPY .user/.config/systemd/user/gnome-bootstrap-rdp.service /home/${GM_RDP_USER}/.config/systemd/user/gnome-bootstrap-rdp.service
COPY .user/.config/systemd/user/gnome-monitor-trigger.service /home/${GM_RDP_USER}/.config/systemd/user/gnome-monitor-trigger.service
COPY .user/.config/systemd/user/gnome-session-ctl.service /home/${GM_RDP_USER}/.config/systemd/user/gnome-session-ctl.service
COPY .user/.config/systemd/user/gnome-session-signal-init.service /home/${GM_RDP_USER}/.config/systemd/user/gnome-session-signal-init.service
COPY .user/.config/systemd/user/gnome-session.service /home/${GM_RDP_USER}/.config/systemd/user/gnome-session.service
COPY .user/.config/systemd/user/gnome-settings-daemon.target /home/${GM_RDP_USER}/.config/systemd/user/gnome-settings-daemon.target
COPY .user/.config/systemd/user/gsd-a11y-settings.service /home/${GM_RDP_USER}/.config/systemd/user/gsd-a11y-settings.service
COPY .user/.config/systemd/user/gsd@.service /home/${GM_RDP_USER}/.config/systemd/user/gsd@.service
COPY .user/.config/systemd/user/gnome-remote-desktop-headless.service.d/override.conf /home/${GM_RDP_USER}/.config/systemd/user/gnome-remote-desktop-headless.service.d/override.conf
COPY .user/.config/systemd/user/org.gnome.Shell@user.service.d/override.conf /home/${GM_RDP_USER}/.config/systemd/user/org.gnome.Shell@user.service.d/override.conf

RUN xdg-user-dirs-update

# Enable services
RUN mkdir -p ~/.config/systemd/user/default.target.wants && \
    mkdir -p ~/.config/systemd/user/gnome-settings-daemon.target.wants && \
    ln -sf /home/${GM_RDP_USER}/.config/systemd/user/gnome-session.service \
    ~/.config/systemd/user/default.target.wants/gnome-session.service && \
    ln -sf /home/${GM_RDP_USER}/.config/systemd/user/gnome-session-ctl.service \
    ~/.config/systemd/user/default.target.wants/gnome-session-ctl.service && \
    ln -sf /home/${GM_RDP_USER}/.config/systemd/user/gnome-settings-daemon.target \
    ~/.config/systemd/user/default.target.wants/gnome-settings-daemon.target && \
    ln -sf /home/${GM_RDP_USER}/.config/systemd/user/gnome-monitor-trigger.service \
    ~/.config/systemd/user/default.target.wants/gnome-monitor-trigger.service && \
    ln -sf /home/${GM_RDP_USER}/.config/systemd/user/gnome-bootstrap-rdp.service \
    ~/.config/systemd/user/default.target.wants/gnome-bootstrap-rdp.service && \
    ln -sf /usr/lib/systemd/user/gnome-remote-desktop-headless.service \
    ~/.config/systemd/user/default.target.wants/gnome-remote-desktop-headless.service

# Enable all gsd components
RUN for gsd in a11y-settings color datetime housekeeping keyboard media-keys power print-notifications rfkill screensaver-proxy sharing smartcard sound usb-protection wacom xsettings; do \
    ln -sf /home/${GM_RDP_USER}/.config/systemd/user/gsd@.service \
    ~/.config/systemd/user/gnome-settings-daemon.target.wants/gsd@$gsd.service; \
    done

COPY .user/.local/bin/gnome-bootstrap-rdp.sh /home/${GM_RDP_USER}/.local/bin/gnome-bootstrap-rdp.sh
COPY .user/.local/bin/gnome-monitor-trigger.sh /home/${GM_RDP_USER}/.local/bin/gnome-monitor-trigger.sh
COPY .user/.bashrc /home/${GM_RDP_USER}/.bashrc

RUN sed -i "s|%GM_RDP_USER%|${GM_RDP_USER}|g" /home/${GM_RDP_USER}/.config/systemd/user/gnome-bootstrap-rdp.service && \
    sed -i "s|%GM_RDP_USER%|${GM_RDP_USER}|g" /home/${GM_RDP_USER}/.config/systemd/user/gnome-monitor-trigger.service && \
    sed -i "s|%GM_LIBEXEC_DIR%|/usr/libexec|g" /home/${GM_RDP_USER}/.config/systemd/user/gnome-session-signal-init.service && \
    sed -i "s|%GM_GNOME_SESSION_BINARY%|/usr/bin/gnome-session|g" /home/${GM_RDP_USER}/.config/systemd/user/gnome-session.service && \
    sed -i "s|%GM_LIBEXEC_DIR%|/usr/libexec|g" /home/${GM_RDP_USER}/.config/systemd/user/gnome-session-ctl.service && \
    sed -i "s|%GM_LIBEXEC_DIR%|/usr/libexec|g" /home/${GM_RDP_USER}/.config/systemd/user/gsd@.service && \
    sed -i "s|%GM_LIBEXEC_DIR%|/usr/libexec|g" /home/${GM_RDP_USER}/.config/systemd/user/gsd-a11y-settings.service

USER root
WORKDIR /home/${GM_RDP_USER}

RUN chmod +x /home/${GM_RDP_USER}/.local/bin/gnome-bootstrap-rdp.sh /home/${GM_RDP_USER}/.local/bin/gnome-monitor-trigger.sh

COPY entrypoint.sh /entrypoint.sh
RUN sed -i "s|%GM_SYSTEMD_BINARY%|/usr/lib/systemd/systemd|g" /entrypoint.sh
COPY healthcheck-rdp.sh /usr/local/bin/healthcheck-rdp.sh
RUN chmod +x /entrypoint.sh /usr/local/bin/healthcheck-rdp.sh

EXPOSE ${GM_RDP_DOCKER_PORT}

HEALTHCHECK --interval=2s --timeout=3s --start-period=5s --retries=30 \
    CMD /usr/local/bin/healthcheck-rdp.sh

CMD ["/entrypoint.sh"]
