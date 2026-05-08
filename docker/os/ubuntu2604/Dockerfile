# syntax=docker/dockerfile:1
FROM ubuntu:26.04

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

ENV UBUNTU_FRONTEND=noninteractive
STOPSIGNAL SIGRTMIN+3

COPY overrides/ubuntu2604/etc/apt/sources.list.d/ubuntu.sources /etc/apt/sources.list.d/ubuntu.sources

RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    userdel -r ubuntu 2>/dev/null || true && \
    sed -i 's|^path-exclude /usr/share/locale/\*|#path-exclude /usr/share/locale/*|' /etc/dpkg/dpkg.cfg.d/* && \
    apt-get update && apt-get upgrade -y && \
    apt-get install -y --no-install-recommends \
        locales \
        keyboard-configuration \
        tzdata \
        wget \
        ca-certificates

RUN sed -i -E "s/^# (($(echo "$GM_LOCALES" | sed 's/,/|/g'))) UTF-8$/\1 UTF-8/" /etc/locale.gen && \
    locale-gen && \
    update-locale LANG=${GM_LOCALE} LANGUAGE=${GM_LANGUAGE} && \
    ln -sf /usr/share/zoneinfo/${GM_TIMEZONE} /etc/localtime && \
    echo "${GM_TIMEZONE}" > /etc/timezone && \
    echo "${GM_HOSTNAME}" > /etc/hostname && \
    echo "XKBMODEL=\"${GM_KEYMODEL}\"" > /etc/default/keyboard && \
    echo "XKBLAYOUT=\"${GM_KEYMAP}\"" >> /etc/default/keyboard && \
    echo 'XKBVARIANT=""' >> /etc/default/keyboard && \
    echo 'XKBOPTIONS=""' >> /etc/default/keyboard && \
    echo 'BACKSPACE="guess"' >> /etc/default/keyboard

ENV TIMEZONE=${GM_TIMEZONE}
ENV LC_ALL=${GM_LOCALE}
ENV LANGUAGE=${GM_LANGUAGE}
ENV LANG=${GM_LOCALE}
ENV GM_RDP_DOCKER_PORT=${GM_RDP_DOCKER_PORT}
ENV GM_RDP_USER=${GM_RDP_USER}
ENV GM_RDP_USER_UID=${GM_RDP_USER_UID}

RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    apt-get update && \
    apt-get install -y --no-install-recommends \
        debconf-i18n \
        "language-pack-${GM_KEYMAP}" \
        "language-pack-${GM_KEYMAP}-base" \
        "language-pack-gnome-${GM_KEYMAP}" \
        "language-pack-gnome-${GM_KEYMAP}-base" \
        systemd \
        systemd-sysv \
        bash \
        bash-completion \
        ncurses-bin \
        libnotify-bin \
        libvte-2.91-common \
        coreutils \
        dbus \
        dbus-user-session \
        libpam-systemd \
        gnome-shell \
        gnome-shell-common \
        gnome-session \
        gnome-session-bin \
        gnome-session-common \
        gnome-settings-daemon \
        gnome-remote-desktop \
        pipewire \
        pipewire-pulse \
        wireplumber \
        mesa-vulkan-drivers \
        libgl1-mesa-dri \
        gsettings-desktop-schemas \
        dconf-cli \
        openssl \
        fuse3 \
        sudo \
        procps \
        iproute2 \
        xdg-desktop-portal \
        xdg-desktop-portal-gnome \
        xdg-desktop-portal-gtk \
        xdg-user-dirs \
        at-spi2-core \
        polkitd \
        pkexec \
        gnome-keyring \
        gnome-keyring-pkcs11 \
        jq

RUN --mount=type=cache,target=/var/cache/apt,sharing=locked \
    --mount=type=cache,target=/var/lib/apt,sharing=locked \
    apt-get update && \
    apt-get install -y --no-install-recommends \
        gnome-control-center \
        fastfetch \
        xdg-desktop-portal-gtk \
        gnome-shell-extensions \
        gnome-shell-extension-prefs \
        curl \
        bzip2 \
        flatpak \
        gnome-software \
        gnome-software-common \
        gnome-software-plugin-snap \
        gnome-software-plugin-flatpak \
        gnome-user-share \
        ptyxis \
        geoclue-2.0 \
        evolution-data-server \
        bolt \
        gnome-tweaks gnome-system-monitor \
        nano \
        file \
        dialog \
        papirus-icon-theme \
        libwebkitgtk-6.0-4 \
        libjavascriptcoregtk-6.0-1 \
        ibus \
        ibus-data \
        ibus-gtk \
        ibus-gtk3 \
        ibus-gtk4 \
        dotnet-sdk-10.0

RUN systemctl mask \
        getty@tty1.service \
        getty-static.service \
        systemd-firstboot.service \
        systemd-udevd.service \
        apt-daily.service \
        apt-daily-upgrade.service \
        e2scrub_reap.service || true

RUN groupadd --gid ${GM_RDP_USER_GID} ${GM_RDP_USER} && \
    useradd \
        --uid ${GM_RDP_USER_UID} \
        --gid ${GM_RDP_USER_GID} \
        --create-home \
        --home-dir /home/${GM_RDP_USER} \
        --shell /bin/bash \
        ${GM_RDP_USER} && \
    usermod -aG sudo,audio,video,plugdev,geoclue ${GM_RDP_USER}

RUN sed -i 's|^%sudo[[:space:]]*ALL=(ALL:ALL)[[:space:]]*ALL|%sudo ALL=(ALL:ALL) NOPASSWD:ALL|' /etc/sudoers

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
