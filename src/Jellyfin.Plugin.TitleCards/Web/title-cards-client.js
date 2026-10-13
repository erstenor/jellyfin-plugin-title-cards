// Title Cards: adds "Show title cards" and "Crop covers to fill their tiles" to Settings > Display
// in the web client. Choices are saved on the server, so they apply in every app the user signs in to.
// Works with the React Display page (Jellyfin 12.2) and the older template-based one.
(function () {
    'use strict';

    var CHECKED_ICON = 'M19 3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.11 0 2-.9 2-2V5c0-1.1-.89-2-2-2zm-9 14l-5-5 1.41-1.41L10 14.17l7.59-7.59L19 8l-9 9z';
    var UNCHECKED_ICON = 'M19 5v14H5V5h14m0-2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2z';

    var SETTINGS = [
        {
            field: 'titleCardsShow',
            key: 'ShowTitleCards',
            label: 'Show title cards',
            describe: function (p) {
                return 'Library images on the home screen are collages of each library\'s artwork. Turn off to see '
                    + 'Jellyfin\'s own library images. Saved straight away and applies in all your apps, including TV '
                    + 'apps, the next time their home screen loads.'
                    + (p.IsDefault ? '' : ' Server default: ' + (p.ServerDefault ? 'on' : 'off') + '.');
            }
        },
        {
            field: 'titleCardsCrop',
            key: 'CropToFill',
            label: 'Crop covers to fill their tiles',
            describe: function (p) {
                return 'Album, book and audiobook covers fill their tiles edge to edge, losing their top and bottom. '
                    + 'Off: each cover is shown whole on a blurred copy of itself.'
                    + (p.CropIsDefault ? '' : ' Server default: ' + (p.ServerCropDefault ? 'on' : 'off') + '.');
            },
            // Only meaningful while title cards are shown.
            enabled: function (p) { return !!p.ShowTitleCards; }
        }
    ];

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

    // Copies the page's own "blurred placeholders" row, so the settings look like their neighbours.
    function buildFromReactRow(row, setting) {
        var copy = row.cloneNode(true);
        copy.classList.add('titleCardsSetting');
        var input = copy.querySelector('input');
        input.name = setting.field;
        input.id = setting.field;
        input.removeAttribute('checked');
        var help = copy.querySelector('.MuiFormHelperText-root, p');
        if (help) {
            help.id = setting.field + '-description';
        }

        var label = copy.querySelector('label');
        if (label) {
            label.setAttribute('aria-describedby', setting.field + '-description');
        }

        var text = copy.querySelector('.MuiFormControlLabel-label');
        if (text) {
            text.textContent = setting.label;
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
                copy.style.opacity = input.disabled ? '0.6' : '';
                if (path) {
                    path.setAttribute('d', input.checked ? CHECKED_ICON : UNCHECKED_ICON);
                }
            }
        };
    }

    function buildLegacy(setting) {
        var element = document.createElement('div');
        element.className = 'checkboxContainer checkboxContainer-withDescription titleCardsSetting';
        element.innerHTML = '<label><input type="checkbox" is="emby-checkbox" id="' + setting.field + '" />'
            + '<span>' + setting.label + '</span></label>'
            + '<div class="fieldDescription checkboxFieldDescription"></div>';
        return {
            element: element,
            input: element.querySelector('input'),
            help: element.querySelector('.fieldDescription'),
            render: function () {}
        };
    }

    function attach(build, anchor) {
        var rows = SETTINGS.map(function (setting) {
            var row = build(setting);
            row.setting = setting;
            return row;
        });
        rows.reduce(function (previous, row) {
            previous.parentNode.insertBefore(row.element, previous.nextSibling);
            return row.element;
        }, anchor);

        var current = null;
        function show(preference) {
            current = preference;
            rows.forEach(function (row) {
                row.input.checked = !!preference[row.setting.key];
                row.input.disabled = row.setting.enabled ? !row.setting.enabled(preference) : false;
                if (row.help) {
                    row.help.textContent = row.setting.describe(preference);
                }

                row.render();
            });
        }

        function busy() {
            rows.forEach(function (row) {
                row.input.disabled = true;
                row.render();
            });
        }

        busy();
        request('GET').then(show, function () {
            rows.forEach(function (row) { row.element.remove(); });
        });

        rows.forEach(function (row) {
            row.input.addEventListener('change', function (event) {
                // The page's own form must not see these fields.
                event.stopPropagation();
                var change = {};
                change[row.setting.key] = row.input.checked;
                busy();
                request('POST', change).then(show, function () {
                    if (current) {
                        show(current);
                    }
                });
            });
        });
    }

    function check() {
        if (!isDisplaySettings() || !window.ApiClient || editingAnotherUser() || document.getElementById(SETTINGS[0].field)) {
            return;
        }

        var reactField = document.querySelector('input[name="enableBlurHash"]');
        var reactRow = reactField && reactField.closest('.MuiFormControl-root');
        if (reactRow) {
            attach(function (setting) { return buildFromReactRow(reactRow, setting); }, reactRow);
            return;
        }

        var legacyField = document.querySelector('#chkBlurhash, #chkFadein');
        var legacyRow = legacyField && legacyField.closest('.checkboxContainer');
        if (legacyRow) {
            attach(buildLegacy, legacyRow);
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
