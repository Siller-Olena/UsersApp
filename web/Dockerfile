FROM nginx:1.27-alpine
COPY index.html app.js style.css config.js /usr/share/nginx/html/
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
ENV API_UPSTREAM=usersapp-api:8080
EXPOSE 80
