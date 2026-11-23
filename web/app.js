const API = (window.APP_CONFIG && window.APP_CONFIG.apiUrl) || "/api";

const form = document.getElementById("user-form");
const idInput = document.getElementById("user-id");
const nameInput = document.getElementById("user-name");
const emailInput = document.getElementById("user-email");
const saveBtn = document.getElementById("save-btn");
const cancelBtn = document.getElementById("cancel-btn");
const tbody = document.getElementById("users-body");
const message = document.getElementById("message");

function showError(text) { message.textContent = text || ""; }

async function request(path, options) {
  const response = await fetch(API + path, options);
  if (!response.ok) throw new Error(`Помилка ${response.status}`);
  return response.status === 204 ? null : response.json();
}

async function loadUsers() {
  try {
    const users = await request("/users");
    tbody.innerHTML = "";
    for (const u of users) {
      const tr = document.createElement("tr");
      for (const value of [u.id, u.name, u.email]) {
        const td = document.createElement("td");
        td.textContent = value;
        tr.appendChild(td);
      }
      const actions = document.createElement("td");
      const edit = document.createElement("button");
      edit.textContent = "Редагувати";
      edit.onclick = () => startEdit(u);
      const del = document.createElement("button");
      del.textContent = "Видалити";
      del.onclick = () => removeUser(u.id);
      actions.append(edit, del);
      tr.appendChild(actions);
      tbody.appendChild(tr);
    }
    showError("");
  } catch (e) {
    showError("Не вдалося завантажити список: " + e.message);
  }
}

function startEdit(user) {
  idInput.value = user.id;
  nameInput.value = user.name;
  emailInput.value = user.email;
  saveBtn.textContent = "Зберегти";
  cancelBtn.hidden = false;
}

function resetForm() {
  form.reset();
  idInput.value = "";
  saveBtn.textContent = "Додати";
  cancelBtn.hidden = true;
}

async function removeUser(id) {
  try {
    await request(`/users/${id}`, { method: "DELETE" });
    await loadUsers();
  } catch (e) {
    showError("Не вдалося видалити: " + e.message);
  }
}

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  const body = JSON.stringify({ name: nameInput.value, email: emailInput.value });
  const headers = { "Content-Type": "application/json" };
  try {
    if (idInput.value) {
      await request(`/users/${idInput.value}`, { method: "PUT", headers, body });
    } else {
      await request("/users", { method: "POST", headers, body });
    }
    resetForm();
    await loadUsers();
  } catch (e) {
    showError("Не вдалося зберегти: " + e.message);
  }
});

cancelBtn.addEventListener("click", resetForm);
loadUsers();
