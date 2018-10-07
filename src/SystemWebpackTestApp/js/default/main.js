import {sayHello} from './hello-sayer';
import '~/css/main.css';

window.addEventListener('DOMContentLoaded', function() {
    const btn = document.getElementById('hello-button');
    let evListener = sayHello;

    btn.addEventListener('click', evListener);

    if (module.hot) {
        module.hot.accept('./hello-sayer', () => {
            btn.removeEventListener('click', evListener);

            // Hook-up button to new handler
            evListener = sayHello;
            btn.addEventListener('click', evListener);

            btn.innerText += ' - HMR!';
        });
    }
});