// Title Cards: adds "Show title cards" to Settings > Display in the web client.
// The choice is saved on the server, so it applies in every app the user signs in to.
// Works with the React Display page (Jellyfin 12.2) and the older template-based one.
(function () {
    'use strict';

    var FIELD = 'titleCardsShow';
    var LABEL = 'Show title cards';
    var CHECKED_ICON = 'M19 3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.11 0 2-.9 2-2V5c0-1.1-.89-2-2-2zm-9 14l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z';
    var UNCHECKED_ICON = 'M19 5v14H5V5h14m0-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2z';

    function isDisplaySettings() {
        return /mypreferencesdisplay/i.test(window.location.hash || window.location.pathname);
    }

    function editingAnotherUser() {
        var match = /[?&]userId=([0-9a-f-]+)/i.exec(window.location.hash || window.location.search);
        return match && window.ApiClient && match[1].replace(/-/g, '').toLowerCase()
            !== String(window.ApiClient.getCurrentUserId()).replace(/-/g, '').toLowerCase();
    }

    function request(method, body) {
        return window.ApiClient.ajax({
            type: method,
            url: window.ApiClient.getUrl('TitleCards/Preference'),
            dataType: 'json',
            contentType: 'application/json',
            data: body ? JSON.stringify(body) : undefined
        });
    }

    function describe(preference) {
        return 'Library images on the home screen are collages of each library\'s artwork. Turn off to see '
            + 'Jellyfin\'s own library images. Saved straight away and applies in all your apps, including TV '
            + 'apps, the next time their home screen loads.'
            + (preference.IsDefault ? '' : ' Server default: ' + (preference.ServerDefault ? 'on' : 'off') + '.');
    }

    // Copies the page's own "blurred placeholders" row, so the setting looks like its neighbours.
    function buildFromReactRow(row) {
        var copy = row.cloneNode(true);
        copy.classList.add('titleCardsSetting');
        var input = copy.querySelector('input');
        input.name = FIELD;
        input.id = FIELD;
        input.removeAttribute('checked');
        var help = copy.querySelector('.MuiFormHelperText-root, p');
        if (help) {
            help.id = FIELD + '-description';
        }

        var label = copy.querySelector('label');
        if (label) {
            label.setAttribute('aria-describedby', FIELD + '-description');
        }

        var text = copy.querySelector('.MuiFormControlLabel-label');
        if (text) {
            text.textContent = LABEL;
        }

        var box = input.parentNode;
        var path = copy.querySelector('svg path');
        return {
            element: copy,
            input: input,
            help: help,
            render: function () {
                box.classList.toggle('Mui-checked', input.checked);
                box.classList.toggle('Mui-disabled', input.disabled);
                if (path) {
                    path.setAttribute('d', input.checked ? CHECKED_ICON : UNCHECKED_ICON);
                }
            }
        };
    }

    function buildLegacy() {
        var element = document.createElement('div');
        element.className = 'checkboxContainer checkboxContainer-withDescription titleCardsSetting';
        element.innerHTML = '<label><input type="checkbox" is="emby-checkbox" id="' + FIELD + '" />'
            + '<span>' + LABEL + '</span></label>'
            + '<div class="fieldDescription checkboxFieldDescription"></div>';
        return {
            element: element,
            input: element.querySelector('input'),
            help: element.querySelector('.fieldDescription'),
            render: function () {}
        };
    }

    function attach(setting, anchor) {
        anchor.parentNode.insertBefore(setting.element, anchor.nextSibling);
        var input = setting.input;

        function show(preference) {
            input.checked = !!preference.ShowTitleCards;
            input.disabled = false;
            if (setting.help) {
                setting.help.textContent = describe(preference);
            }

            setting.render();
        }

        input.disabled = true;
        setting.render();
        request('GET').then(show, function () {
            setting.element.remove();
        });

        input.addEventListener('change', function (event) {
            // The page's own form must not see this field.
            event.stopPropagation();
            var wanted = input.checked;
            input.disabled = true;
            setting.render();
            request('POST', { ShowTitleCards: wanted }).then(show, function () {
                input.checked = !wanted;
                input.disabled = false;
                setting.render();
            });
        });
    }

    function check() {
        if (!isDisplaySettings() || !window.ApiClient || editingAnotherUser() || document.getElementById(FIELD)) {
            return;
        }

        var reactField = document.querySelector('input[name="enableBlurHash"]');
        var reactRow = reactField && reactField.closest('.MuiFormControl-root');
        if (reactRow) {
            attach(buildFromReactRow(reactRow), reactRow);
            return;
        }

        var legacyField = document.querySelector('#chkBlurhash, #chkFadein');
        var legacyRow = legacyField && legacyField.closest('.checkboxContainer');
        if (legacyRow) {
            attach(buildLegacy(), legacyRow);
        }
    }

    var pending = false;
    new MutationObserver(function () {
        if (!pending) {
            pending = true;
            window.requestAnimationFrame(function () {
                pending = false;
                check();
            });
        }
    }).observe(document.documentElement, { childList: true, subtree: true });
    check();
}());
